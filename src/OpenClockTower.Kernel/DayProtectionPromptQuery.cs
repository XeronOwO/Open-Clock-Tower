namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人投影用查询：当前开放流放是否到了「该给这一席做死亡保护裁定」的时刻（R-0048）。
/// </summary>
/// <remarks>
/// <para>
/// 只有「裁定真的会决定这次流放结果」时才有提示——收票收完、票面达线、目标存活、尚未裁定，且保护来源
/// 要求说书人裁定（<see cref="DeathProtectionOutcome.NeedsRuling"/>）或维度观测不齐
/// （<see cref="DeathProtectionOutcome.Indeterminate"/>）。其余情形返回 null：平台不提前提问、不预缓存
/// （R-0048 第 2 条），界面上也不该出现必被服务端拒绝的入口。
/// </para>
/// <para>
/// 受理条件与 <see cref="DayProtectionMachine"/> 同源（<see cref="DayProtectionEligibility"/>）；
/// 纯查询：不产事件、不写账。
/// </para>
/// </remarks>
public static class DayProtectionPromptQuery
{
    /// <summary>返回提示；当前没有该给的提示时为 null。</summary>
    public static DayProtectionPrompt? ForOpenExile(SettlementContext context, DayState? dayState)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (dayState?.OpenDay is not { } day || day.OpenExile is not { } exile)
        {
            return null;
        }

        var eligibility = DayProtectionEligibility.Assess(context, day, exile.Target);
        return eligibility.Kind switch
        {
            DayProtectionEligibility.Kind.NeedsRuling => Prompt(DeathProtectionOutcome.NeedsRuling),
            DayProtectionEligibility.Kind.Indeterminate => Prompt(DeathProtectionOutcome.Indeterminate),
            _ => null,
        };

        DayProtectionPrompt Prompt(DeathProtectionOutcome outcome) => new()
        {
            Seat = exile.Target,
            Outcome = outcome,
            Note = eligibility.Message,
        };
    }
}
