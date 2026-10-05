namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者的白天提问：玩家主动命令 → 请求进事件流 → 开归属席位的裁定点 →
/// 说书人给出两条信息（一真一假）→ 两条信息只到本人。每个白天一次。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ArtistQuestionMachine"/> 同构：内核只管状态机（阶段校验、挂起、结清、记账），
/// 规则语义由 <see cref="ISavantQuestionSource"/>（规则层实现）给出——内核不认角色 slug（D-0008）。
/// </para>
/// <para>
/// 两条硬口径（R-0057）：提问由玩家主动发起，**每个白天一次**（用度按白天记账，阶段边界清零）；
/// 平台**不判定哪条信息为真**（D-0002）——两条都标「可能为假」，由说书人掌握真假；
/// 能力未生效（醉酒 / 中毒 / 死亡）时照样可以给（可能两条都真或都假，百科《博学者》· 角色简介）。
/// 未结清的提问**不能跨阶段**（<see cref="SavantQuestionFolder"/> 显式失败）。
/// </para>
/// </remarks>
internal static class SavantQuestionMachine
{
    /// <summary>
    /// 结清用的稳定裁定点标识：同一白天内固定（同一时刻最多一条进行中提问，不会混淆历史）。
    /// </summary>
    internal static DecisionPointId DecisionIdOf(StepMachineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new DecisionPointId($"{state.Plan.Label}:savant.question");
    }

    /// <summary>处理一条「博学者要信息」输入。</summary>
    internal static StepMachineOutcome Ask(
        StepMachineState state,
        SettlementContext context,
        AskSavantQuestionInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.Plan.Phase != GamePhase.Day || state.Day?.OpenDay is null)
        {
            return Reject(
                state,
                "savant.not_open_day",
                "现在是夜晚，或白天已经结束：博学者只能在白天要信息");
        }

        if (state.SavantQuestion is not null)
        {
            return Reject(
                state,
                "savant.question_pending",
                "已经有一条博学者提问在等待说书人给出两条信息：先结清它，或由说书人强推作废");
        }

        if (state.AwaitingDecision is not null)
        {
            return Reject(state, "savant.decision_pending", "还有别的裁定点没有结清：先结清再来要信息");
        }

        if (state.SavantAskedSeat == input.Seat)
        {
            return Reject(
                state,
                "savant.already_asked_today",
                "博学者每个白天只能要一次信息：今天已经要过了（R-0057）");
        }

        var character = context.State.Seat(input.Seat)?.CharacterValue;
        if (character is not { } seatCharacter)
        {
            return Reject(
                state,
                "savant.character_unobserved",
                $"{input.Seat.Value} 号的角色还没有观测：无法确认是不是博学者（不猜，D-0015）");
        }

        var source = context.SavantQuestions.FirstOrDefault(candidate => candidate.Character == seatCharacter);
        if (source is null)
        {
            return Reject(
                state,
                "savant.not_savant",
                $"{input.Seat.Value} 号（{seatCharacter.Value}）不是可以向说书人要两条信息的博学者");
        }

        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("事件流损坏：白天打开却没有当前槽位");

        var events = new List<GameEvent>
        {
            new SavantQuestionAskedEvent
            {
                Seat = input.Seat,
                Character = seatCharacter,
            },
            new DecisionPointRaisedEvent
            {
                SlotId = slot.Id,
                AttributionSeat = input.Seat,
                DecisionPoint = new DecisionPoint
                {
                    Id = DecisionIdOf(state),
                    Prompt = source.BuildPrompt(),
                },
            },
        };

        return Applied(state, events);
    }

    /// <summary>结清一条博学者提问的裁定（由 <see cref="StepMachine"/> 的裁定结清路径分派）。</summary>
    internal static StepMachineOutcome Resolve(
        StepMachineState state,
        SettlementContext context,
        SavantQuestion question,
        string? decision,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(question);

        // 来源检索键用**提问时刻的角色快照**：挂起期间角色被换走也不该让结清卡死
        // （与艺术家提问同姿态）。
        var source = context.SavantQuestions
            .FirstOrDefault(candidate => candidate.Character == question.Character);
        if (source is null)
        {
            return Reject(
                state,
                "savant.source_unknown",
                "本局没有登记该角色（提问时刻）的博学者提问来源契约：内核不自行认定角色规则");
        }

        if (string.IsNullOrWhiteSpace(decision))
        {
            return Reject(
                state,
                "savant.decision_missing",
                "没有给出两条信息：请把两条用「|」分开写清楚（例如：3 号是镇民|5 号是爪牙）");
        }

        var resolution = source.Resolve(new SavantQuestionResolutionContext
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
                "savant.indeterminate",
                "无法判定这次信息怎么记账（席位的生死 / 醉酒 / 中毒还没有观测齐）："
                + "先上报状态，再裁定（不猜，D-0015）");
        }

        if (resolution.Ruling == SavantQuestionRuling.Invalid)
        {
            // 自由文本是用户输入：不合格式要给一条可读的拒绝，而不是抛异常（R-0057）。
            return Reject(state, "savant.decision_invalid", resolution.Note ?? "裁定文本不合格式");
        }

        if (resolution.Ruling != SavantQuestionRuling.Answered)
        {
            throw new InvalidOperationException($"未知的博学者提问裁决：{resolution.Ruling}");
        }

        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("事件流损坏：博学者提问挂起却没有当前槽位");

        // 顺序：先收裁定点（通用挂起）、再清提问、最后记账 / 下发信息。
        var events = new List<GameEvent>
        {
            new DecisionPointResolvedEvent
            {
                DecisionPointId = DecisionIdOf(state),
                Decision = decision,
                Note = note,
            },
            new SavantQuestionClosedEvent
            {
                Seat = question.Seat,
                Closure = SavantQuestionClosure.Answered,
            },
            new AbilityResolvedEvent
            {
                SlotId = slot.Id,
                Actor = question.Seat,
                Ability = source.Ability,
                Effective = resolution.Effective,
                Malfunctions = resolution.Malfunctions,
                Note = resolution.Note,
            },
        };

        events.AddRange(resolution.Events);
        return Applied(state, events);
    }

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理博学者提问后丢失步骤机状态"),
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
