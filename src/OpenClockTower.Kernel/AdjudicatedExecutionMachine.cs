namespace OpenClockTower.Kernel;

/// <summary>
/// 处罚处决（说书人主动处决）：洗脑师 / 畸形秀演员的"疯狂"后果，绕过提名流程。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0020：
/// 白天形态占用当天处决上限并立即收口白天（直接进入夜晚，与 <see cref="DayMachine.CloseDay"/> 同一形状）；
/// 夜晚形态不写白天账、不推进阶段，也不占下一个白天的上限（《畸形秀演员》的明文例外）；
/// 已死亡的目标只记「被处决」、不重复记死亡（处决 ≠ 死亡，百科《处决》）。
/// </para>
/// <para>
/// 依据是否成立由规则层契约 <see cref="IAdjudicatedExecutionSource"/> 给出（内核不认角色 slug）；
/// 不成立或判定不了时**显式拒绝、不产出任何事件**（R-0020 / D-0015：不猜）。
/// </para>
/// <para>
/// 存活目标是否真的死亡，先问统一死亡保护查询（死因 <see cref="DeathProtectionCause.Execution"/>，
/// R-0048 第 6 条 / R-0020 第 7 条）：受保护只记「被处决」、不产生死亡；待裁定 / 判定不了整条拒绝——
/// 与 <see cref="DayMachine.CloseDay"/> 的处决收口同源。
/// </para>
/// </remarks>
internal static class AdjudicatedExecutionMachine
{
    /// <summary>处理一条处罚处决输入。</summary>
    /// <param name="state">当前步骤机状态。</param>
    /// <param name="context">结算上下文（账、座次、依据契约）。</param>
    /// <param name="input">处罚处决输入。</param>
    internal static StepMachineOutcome Handle(
        StepMachineState state,
        SettlementContext context,
        PunishExecutionInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (!context.Seats.Contains(input.Seat))
        {
            return Reject(state, "punishment.seat_unknown", $"席位 {input.Seat.Value} 不在本局座次里");
        }

        var source = context.AdjudicatedExecutions
            .FirstOrDefault(candidate => candidate.Source == input.Source);
        if (source is null)
        {
            return Reject(
                state,
                "punishment.source_unknown",
                $"本局没有登记处罚来源 {input.Source} 的依据契约：内核不自行认定角色规则");
        }

        var eligibility = source.Evaluate(context.State, context.Seats, input.Seat)
            ?? throw new InvalidOperationException($"处罚处决契约 {input.Source} 返回了 null：没有结论时必须显式给出不成立");

        if (eligibility.Applicable is not true)
        {
            return Reject(
                state,
                eligibility.Applicable is null ? "punishment.indeterminate" : "punishment.not_applicable",
                eligibility.Note);
        }

        var day = state.Day?.OpenDay;
        var duringDay = state.Plan.Phase == GamePhase.Day && day is not null;

        if (duringDay)
        {
            // 艺术家的提问未结清时不能收口白天（R-0040）：否则问题会跨到夜晚 / 残留。
            if (state.ArtistQuestion is not null)
            {
                return Reject(
                    state,
                    "day.artist_question_pending",
                    "还有艺术家的提问没有结清：先由说书人回答（或要求重问 / 强推作废），再处罚处决");
            }

            // 博学者的提问未结清时同样不能收口白天（R-0057）。
            if (state.SavantQuestion is not null)
            {
                return Reject(
                    state,
                    "day.savant_question_pending",
                    "还有博学者的提问没有结清：先由说书人给出两条信息（或强推作废），再处罚处决");
            }

            if (day!.OpenNomination is not null)
            {
                // 与强推同一口径（白天票对抗性复核 F-2）：不替说书人拍板计票结论，
                // 也不让白天账留下一条永远停在投票中的提名（R-0020 第 4 条）。
                return Reject(
                    state,
                    "day.nomination_not_counted",
                    "还有提名没有计票：先计票（计票结论本就由说书人掌握），再处罚处决");
            }

            if (day.HasExecution)
            {
                return Reject(
                    state,
                    "day.execution_used",
                    $"白天 {day.DayNumber} 已经处决过 {day.Executions[0].Seat.Value}：每个白天最多一次处决（R-0020）");
            }
        }

        var life = context.State.Seat(input.Seat)?.LifeValue;
        if (life is null)
        {
            return Reject(
                state,
                "punishment.life_unknown",
                $"席位 {input.Seat.Value} 的生死还没有观测：无法判定处罚处决是否产生死亡（不猜）");
        }

        // 处决只杀非旅行者（R-0049 第 4 条）：处罚处决（含夜晚形态）同样不杀旅行者——旅行者只能被流放
        // 杀死（《旅行者》/ 印刷规则书「Travelers are exiled, not executed」）。事实缺失 / 未观测显式拒绝，不猜。
        if (context.Characters is not { } characters)
        {
            return Reject(
                state,
                "punishment.character_facts_missing",
                "本批没有角色事实端口：无法判定处罚目标是不是旅行者（不猜；R-0049）");
        }

        if (context.State.Seat(input.Seat)?.CharacterValue is not { } targetCharacter)
        {
            return Reject(
                state,
                "punishment.target_character_unknown",
                $"席位 {input.Seat.Value} 的角色还没有观测：无法判定处罚目标是不是旅行者（不猜；R-0049）");
        }

        if (characters.IsTraveller(targetCharacter))
        {
            return Reject(
                state,
                "punishment.target_is_traveller",
                "旅行者是被流放、不是被处决（R-0049）：处罚处决不能杀旅行者");
        }

        var events = new List<GameEvent>
        {
            new ExecutedEvent
            {
                DayNumber = duringDay ? day!.DayNumber : null,
                Seat = input.Seat,
                Kind = KindOf(input.Source),
                Note = input.Note,
            },
        };

        if (life == LifeState.Alive)
        {
            // 存活者被处罚处决是否产生死亡，先问统一死亡保护查询（R-0048 第 6 条 / R-0020 第 7 条，按死因）：
            // 受保护只记「被处决」、不产生死亡；待裁定 / 判定不了整条输入显式拒绝（不猜）。
            // 与 DayMachine.CloseDay 的处决收口同源——两条处决致死路径不允许各判各的。
            var protection = DeathProtectionQuery.Resolve(
                context,
                state.Day?.OpenDay,
                input.Seat,
                DeathProtectionCause.Execution);
            switch (protection.Outcome)
            {
                case DeathProtectionOutcome.Protected:
                    break;
                case DeathProtectionOutcome.NeedsRuling:
                    return Reject(state, "punishment.execution_protection_required", protection.Note);
                case DeathProtectionOutcome.Indeterminate:
                    return Reject(
                        state,
                        "punishment.execution_protection_indeterminate",
                        $"{protection.Note}（先补观测，再处罚处决；R-0048）");
                default:
                    events.Add(new SeatStateChangedEvent
                    {
                        Seat = input.Seat,
                        Life = LifeState.Dead,
                        Reason = eligibility.DeathReason
                            ?? throw new InvalidOperationException($"处罚处决契约 {input.Source} 成立但没有给出死亡原因"),
                        CausedBy = eligibility.CausedBy,
                        EffectId = eligibility.EffectId,
                    });
                    break;
            }
        }

        if (!duringDay)
        {
            // 夜晚（或阶段之间）：不写白天账、不推进阶段——不占任何白天的上限（R-0020）。
            return Applied(state, events);
        }

        events.Add(new DayClosedEvent { DayNumber = day!.DayNumber });

        // 与 CloseDay 同一形状：先落白天账，再推槽位、收计划。
        var after = StepMachineFolder.ApplyAll(state, events)
            ?? throw new InvalidOperationException("事件流损坏：处罚处决后丢失步骤机状态");
        events.Add(new SlotAdvancedEvent { FromIndex = after.SlotIndex, ToIndex = after.SlotIndex + 1 });
        events.Add(new PhaseCompletedEvent { PlanLabel = after.Plan.Label });

        return Applied(state, events);
    }

    private static ExecutionKind KindOf(MadnessPunishmentSource source) => source switch
    {
        MadnessPunishmentSource.Cerenovus => ExecutionKind.CerenovusMadness,
        MadnessPunishmentSource.Mutant => ExecutionKind.MutantMadness,
        _ => throw new InvalidOperationException($"未知的处罚来源：{source}"),
    };

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理处罚处决后丢失步骤机状态"),
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
