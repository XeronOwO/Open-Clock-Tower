namespace OpenClockTower.Kernel;

/// <summary>
/// 「今天的死亡保护」裁定的纯迁移（R-0048）：受理条件、拒绝码与事件产出。
/// </summary>
/// <remarks>
/// <para>
/// **达线时裁定**（票据「D3 实施口径」）：只有「这一票真的会决定生死」时才受理——受理条件评估在
/// <see cref="DayProtectionEligibility"/>（与说书人提示 <see cref="DayProtectionPromptQuery"/> 同源，
/// 防止「机器受理 / 界面不给入口」两处漂移）；**平台不提前提问、不预缓存**；每席位每天至多一条。
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

        var eligibility = DayProtectionEligibility.Assess(context, day, input.Seat);
        if (eligibility.Kind != DayProtectionEligibility.Kind.NeedsRuling)
        {
            return DayOutcome.Reject(eligibility.Code, eligibility.Message);
        }

        return DayOutcome.Accepted(
        [
            new DayProtectionDecidedEvent
            {
                DayNumber = day.DayNumber,
                Seat = input.Seat,
                Protected = input.Protected,
                Note = input.Note,
            },
        ]);
    }
}
