namespace OpenClockTower.Kernel;

/// <summary>
/// 「今天的死亡保护」裁定的纯迁移（R-0048）：受理条件、拒绝码与事件产出。
/// </summary>
/// <remarks>
/// <para>
/// **达线时裁定**（票据「D3 实施口径」）：只有「这一票真的会决定生死」时才受理——
/// 该席位有一条未结清流放、收票已收完、票面达线（赞成 × 2 ≥ 收票席位数），且死亡保护查询当前
/// 返回「待裁定」。平台不提前提问、不预缓存；每席位每天至多一条。
/// </para>
/// <para>
/// **纯计算**（D-0008）：事实从账读，不猜；保护是否与本次死因相关由规则层来源回答（R-0048）。
/// </para>
/// </remarks>
internal static class DayProtectionMachine
{
    /// <summary>受理一条死亡保护裁定输入；不满足受理条件时显式拒绝（不产出任何事件）。</summary>
    internal static DayOutcome Resolve(
        DayState state,
        SettlementContext context,
        ResolveDayProtectionInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (!context.Seats.Contains(input.Seat))
        {
            return DayOutcome.Reject(
                "day.protection_seat_unknown",
                $"席位 {input.Seat.Value} 不在本局在局座次里：不能给它裁定死亡保护");
        }

        if (day.ProtectionDecisionFor(input.Seat) is not null)
        {
            return DayOutcome.Reject(
                "day.protection_already_decided",
                $"席位 {input.Seat.Value} 今天已经裁定过死亡保护：每席位每天至多一条（R-0048）");
        }

        // 达线时裁定的另一半前提是「目标存活」：已死亡的目标不会再死亡，裁定没有意义；
        // 生死没观测齐时不猜（D-0015）——先补观测，再裁定 / 计票。
        var life = context.State.Seat(input.Seat)?.LifeValue;
        if (life is null)
        {
            return DayOutcome.Reject(
                "day.protection_life_unknown",
                $"席位 {input.Seat.Value} 的生死还没有观测：无法受理死亡保护裁定（不猜）");
        }

        if (life != LifeState.Alive)
        {
            return DayOutcome.Reject(
                "day.protection_not_required",
                $"席位 {input.Seat.Value} 已经死亡：死亡保护不会改变结果，不需要裁定（R-0048）");
        }

        if (day.OpenExile is not { } exile || exile.Target != input.Seat)
        {
            return DayOutcome.Reject(
                "day.protection_not_required",
                $"今天没有针对席位 {input.Seat.Value} 的未结清流放：死亡保护裁定只在流放达线时受理（R-0048）");
        }

        if (exile.Sweep is not { IsComplete: true } sweep)
        {
            return DayOutcome.Reject(
                "day.protection_not_required",
                $"第 {exile.Index} 条流放的收票还没有收完：达线之后才需要裁定死亡保护（R-0048）");
        }

        if (exile.Ballot.Count * 2 < sweep.Seats.Count)
        {
            return DayOutcome.Reject(
                "day.protection_not_required",
                $"第 {exile.Index} 条流放没有达线（{exile.Ballot.Count} 票 / {sweep.Seats.Count} 席）："
                    + "不需要裁定死亡保护（R-0048）");
        }

        var assessment = DeathProtectionQuery.Resolve(context, day, input.Seat, DeathProtectionCause.Exile);
        return assessment.Outcome switch
        {
            DeathProtectionOutcome.NeedsRuling => DayOutcome.Accepted(
            [
                new DayProtectionDecidedEvent
                {
                    DayNumber = day.DayNumber,
                    Seat = input.Seat,
                    Protected = input.Protected,
                    Note = input.Note,
                },
            ]),
            DeathProtectionOutcome.Indeterminate => DayOutcome.Reject(
                "day.protection_indeterminate",
                $"{assessment.Note}（判定不了时先补观测，再裁定；R-0048）"),
            _ => DayOutcome.Reject(
                "day.protection_not_required",
                assessment.Note),
        };
    }
}
