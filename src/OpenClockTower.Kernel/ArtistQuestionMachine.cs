namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家的白天提问：玩家主动命令 → 私密问题进事件流 → 开归属席位的裁定点 →
/// 说书人四种回答（是 / 不是 / 我不知道 / 要求重问）→ 信息只到本人（R-0040）。
/// </summary>
/// <remarks>
/// <para>
/// 与处罚处决同构：内核只管状态机（阶段校验、挂起、结清、记账），规则语义由
/// <see cref="IArtistQuestionSource"/>（规则层实现）给出——内核不认角色 slug（D-0008）。
/// </para>
/// <para>
/// 三件硬口径（R-0040）：提问由玩家主动发起（不是服务端推送的等待响应）；
/// 三种回答都记一次能力使用并落「失去能力」标记（未生效也算，百科《重要细节》三-3）；
/// 「要求重问」不记使用。未结清的问题**不能跨阶段**（<see cref="ArtistQuestionFolder"/> 显式失败）。
/// </para>
/// </remarks>
internal static class ArtistQuestionMachine
{
    /// <summary>
    /// 结清用的稳定裁定点标识：同一白天内固定（同一时刻最多一条进行中问题，不会混淆历史）。
    /// </summary>
    internal static DecisionPointId DecisionIdOf(StepMachineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new DecisionPointId($"{state.Plan.Label}:artist.question");
    }

    /// <summary>处理一条「艺术家提问」输入。</summary>
    internal static StepMachineOutcome Ask(
        StepMachineState state,
        SettlementContext context,
        AskArtistQuestionInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.Plan.Phase != GamePhase.Day || state.Day?.OpenDay is null)
        {
            return Reject(
                state,
                "artist.not_open_day",
                "现在是夜晚，或白天已经结束：艺术家的提问只能在白天进行（R-0040）");
        }

        if (state.ArtistQuestion is not null)
        {
            return Reject(
                state,
                "artist.question_pending",
                "已经有一条问题在等待说书人回答：先结清它，或由说书人强推作废");
        }

        if (state.AwaitingDecision is not null)
        {
            return Reject(state, "artist.decision_pending", "还有别的裁定点没有结清：先结清再提问");
        }

        var character = context.State.Seat(input.Seat)?.CharacterValue;
        if (character is not { } seatCharacter)
        {
            return Reject(
                state,
                "artist.character_unobserved",
                $"{input.Seat.Value} 号的角色还没有观测：无法确认是不是艺术家（不猜，D-0015）");
        }

        var source = context.ArtistQuestions.FirstOrDefault(candidate => candidate.Character == seatCharacter);
        if (source is null)
        {
            return Reject(
                state,
                "artist.not_artist",
                $"{input.Seat.Value} 号（{seatCharacter.Value}）不是可以提问的艺术家");
        }

        // 「每局限一次」的放宽（咖啡师「行动两次」R-0052 第 3 条；集骨者「重获能力」R-0054 第 4 条）：
        // 对应窗口**确认生效**且总使用次数还没到 2 时还能再问一次（用过一次 → 再用一次；没用过 →
        // 合计可用两次）。窗口生效与否判定不了时按"不能再问"处理：不猜、也不多给一次机会。
        var uses = context.State.AbilityUses.UseCount(input.Seat, source.Ability);
        var boosted = context.State.WindowOn(input.Seat, EffectWindowKind.SecondAction) == true && uses < 2;
        var regained = context.State.RegainedAbilityOn(input.Seat) == true && uses < 2;
        if (uses > 0 && !boosted && !regained)
        {
            return Reject(
                state,
                "artist.already_used",
                "艺术家的能力已经用过了（每局限一次）：本局不能再提问（R-0040；"
                    + "咖啡师「行动两次」/ 集骨者「重获能力」窗口内、未满两次时可以再用，"
                    + "R-0052 第 3 条 / R-0054 第 4 条）");
        }

        var question = input.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return Reject(state, "artist.question_empty", "问题不能为空");
        }

        if (question.Length > ArtistQuestionText.MaxLength)
        {
            return Reject(
                state,
                "artist.question_too_long",
                $"问题太长（上限 {ArtistQuestionText.MaxLength} 个字符）：请精简后重问");
        }

        // 输入卫生：问题是单行自由文本，控制字符（换行 / 制表 / 其他不可见）一律拒绝
        // （与说书人注记的文本口径同族，见 SeatAnnotationText）。
        if (question.Any(char.IsControl))
        {
            return Reject(state, "artist.question_control", "问题不能包含控制字符：请用普通空格分隔");
        }

        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("事件流损坏：白天打开却没有当前槽位");

        var events = new List<GameEvent>
        {
            new ArtistQuestionAskedEvent
            {
                Seat = input.Seat,
                Character = seatCharacter,
                Question = question,
            },
            new DecisionPointRaisedEvent
            {
                SlotId = slot.Id,
                AttributionSeat = input.Seat,
                DecisionPoint = new DecisionPoint
                {
                    Id = DecisionIdOf(state),
                    Prompt = source.BuildPrompt(question),
                },
            },
        };

        return Applied(state, events);
    }

    /// <summary>结清一条艺术家提问的裁定（由 <see cref="StepMachine"/> 的裁定结清路径分派）。</summary>
    internal static StepMachineOutcome Resolve(
        StepMachineState state,
        SettlementContext context,
        ArtistQuestion question,
        string? decision,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(question);

        // 来源检索键用**提问时刻的角色快照**：挂起期间角色被换走也不该让结清卡死
        // （与槽位结算按槽位 Owner 取契约同姿态；否则只剩强推作废一条路）。
        var source = context.ArtistQuestions
            .FirstOrDefault(candidate => candidate.Character == question.Character);
        if (source is null)
        {
            return Reject(
                state,
                "artist.source_unknown",
                "本局没有登记该角色（提问时刻）的艺术家提问来源契约：内核不自行认定角色规则");
        }

        if (string.IsNullOrWhiteSpace(decision))
        {
            return Reject(state, "artist.decision_missing", "没有给出回答（是 / 不是 / 我不知道 / 要求重问）");
        }

        var resolution = source.Resolve(new ArtistQuestionResolutionContext
        {
            Question = question,
            Decision = decision,
            State = context.State,
            Seats = context.Seats,
        });
        if (resolution is null)
        {
            return Reject(
                state,
                "artist.indeterminate",
                "无法判定这次回答如何记账（席位的生死 / 醉酒 / 中毒还没有观测齐）："
                + "先上报状态，再裁定（不猜，D-0015）");
        }

        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("事件流损坏：艺术家提问挂起却没有当前槽位");

        // 顺序：先收裁定点（通用挂起）、再清问题、最后记账 / 下发信息。
        var events = new List<GameEvent>
        {
            new DecisionPointResolvedEvent
            {
                DecisionPointId = DecisionIdOf(state),
                Decision = decision,
                Note = note,
            },
        };

        switch (resolution.Ruling)
        {
            case ArtistQuestionRuling.Returned:
                events.Add(new ArtistQuestionClosedEvent
                {
                    Seat = question.Seat,
                    Closure = ArtistQuestionClosure.Returned,
                });
                break;

            case ArtistQuestionRuling.Answered:
                events.Add(new ArtistQuestionClosedEvent
                {
                    Seat = question.Seat,
                    Closure = ArtistQuestionClosure.Answered,
                });
                events.Add(new AbilityResolvedEvent
                {
                    SlotId = slot.Id,
                    Actor = question.Seat,
                    Ability = source.Ability,
                    Effective = resolution.Effective,
                    Malfunctions = resolution.Malfunctions,
                    Note = resolution.Note,
                });
                events.AddRange(resolution.Events);
                break;

            default:
                throw new InvalidOperationException($"未知的艺术家提问裁决：{resolution.Ruling}");
        }

        return Applied(state, events);
    }

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理艺术家提问后丢失步骤机状态"),
            Events = events,
        };

    private static StepMachineOutcome Reject(StepMachineState state, string code, string note) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Rejected,
            State = state,
            Events = [],
            RejectionCode = code,
            RejectionNote = note,
        };
}
