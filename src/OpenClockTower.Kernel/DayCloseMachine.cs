namespace OpenClockTower.Kernel;

/// <summary>
/// 「结束白天」的纯迁移（百科《处决》；R-0020 / R-0048 / R-0049 / R-0050）：处决当前「即将被处决」者，
/// 当天首次处决后按规则层来源决定是否打开屠夫额外提名窗口，最后关账。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="DayMachine"/> 拆出（单文件 600 行门禁）：白天规则里最重的两件事——计票与关账——各自成器；
/// <see cref="DayMachine.CloseDay"/> 保留为公开入口（调用点与既有测试断言不变）。
/// </para>
/// <para>
/// 顺序：目标旅行者排除（R-0049）→ 处决事实 → 死亡保护查询（R-0048）→ 首次处决后的屠夫窗口（R-0050）→ 关账。
/// 处罚处决不走这里（见 <see cref="AdjudicatedExecutionMachine"/>，R-0020）。
/// </para>
/// </remarks>
internal static class DayCloseMachine
{
    /// <summary>结束白天的状态迁移；不满足收口条件时显式拒绝（不产出任何事件）。</summary>
    internal static DayOutcome Close(DayState state, SettlementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenNomination is not null)
        {
            return DayOutcome.Reject(
                "day.nomination_not_counted",
                "还有提名没有计票：先把票计完（或由说书人强推兜底），再结束白天");
        }

        if (day.OpenExile is not null)
        {
            return DayOutcome.Reject(
                "day.exile_not_counted",
                "还有流放没有结清：先把流放收完、计票，再结束白天（票据「D2 实施口径」）");
        }

        var events = new List<GameEvent>(capacity: 4);
        SeatId? justExecuted = null;
        if (day.AboutToBeExecuted is { } seat)
        {
            var life = context.State.Seat(seat)?.LifeValue;
            if (life is null)
            {
                return DayOutcome.Reject(
                    "day.executed_life_unknown",
                    $"席位 {seat.Value} 的生死还没有观测：无法判定处决是否产生死亡（不猜）");
            }

            // 处决只杀非旅行者（R-0049 第 4 条）：计票已经保证新流不会把旅行者落靶，这里是防御旧日志 /
            // 损坏流；事实缺失或角色未观测显式拒绝，不猜。说书人可用强推兜底关闭白天（强推不产生处决）。
            if (context.Characters is not { } characters)
            {
                return DayOutcome.Reject(
                    "day.character_facts_missing",
                    "本批没有角色事实端口：无法判定处决目标是不是旅行者（不猜；R-0049）");
            }

            if (context.State.Seat(seat)?.CharacterValue is not { } targetCharacter)
            {
                return DayOutcome.Reject(
                    "day.execution_target_character_unknown",
                    $"席位 {seat.Value} 的角色还没有观测：无法判定处决目标是不是旅行者（不猜；R-0049）");
            }

            if (characters.IsTraveller(targetCharacter))
            {
                return DayOutcome.Reject(
                    "day.execution_target_is_traveller",
                    "旅行者是被流放、不是被处决（R-0049）：请改用流放；确需收尾可由说书人强推关闭白天"
                        + "（强推不产生处决）");
            }

            events.Add(new ExecutedEvent
            {
                DayNumber = day.DayNumber,
                Seat = seat,
                Kind = ExecutionKind.Day,
            });
            justExecuted = seat;

            // 存活者被处决是否产生死亡，先问统一死亡保护查询（R-0048，按死因）：受保护只记「被处决」、
            // 不产生死亡；待裁定 / 判定不了显式拒绝。已经死亡者只记录「被处决」，不重复记死亡。
            // 今日没有覆盖处决路径的保护来源 → 行为与既有实现一致。
            if (life == LifeState.Alive)
            {
                var protection = DeathProtectionQuery.Resolve(context, day, seat, DeathProtectionCause.Execution);
                switch (protection.Outcome)
                {
                    case DeathProtectionOutcome.Protected:
                        break;
                    case DeathProtectionOutcome.NeedsRuling:
                        return DayOutcome.Reject("day.execution_protection_required", protection.Note);
                    case DeathProtectionOutcome.Indeterminate:
                        return DayOutcome.Reject(
                            "day.execution_protection_indeterminate",
                            $"{protection.Note}（先补观测，再结束白天；R-0048）");
                    default:
                        events.Add(new SeatStateChangedEvent
                        {
                            Seat = seat,
                            Life = LifeState.Dead,
                            Reason = DayMachine.ExecutionDeathReason,
                        });
                        break;
                }
            }
        }

        // 屠夫窗口（R-0050 第 1 条）：只跟在当天**首次**处决事实之后问一次——有可用屠夫则开窗、
        // 白天保持 Open；没有则照常关账；判定不了显式拒绝（不猜）。窗口已经开过（含已用）或本次
        // 已经是第二次处决时不再问，直接关账（R-0050 第 2 条）。
        if (justExecuted is { } executedSeat && day.Executions.Count == 0)
        {
            var window = ExtraNominationMachine.OpenAfterFirstExecution(day, context, executedSeat);
            if (window.IsRejected)
            {
                return window;
            }

            if (window.Events.Count > 0)
            {
                events.AddRange(window.Events);
                return DayOutcome.Accepted(events);
            }
        }

        events.Add(new DayClosedEvent { DayNumber = day.DayNumber });
        return DayOutcome.Accepted(events);
    }
}
