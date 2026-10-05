using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 两条候选的组合校验（R-0057-C）：平台防呆 + 真值组合。
/// </summary>
/// <remarks>
/// <para>
/// **两端同一份口径**：说书人端用它显示组合结论并在非法时禁用提交，服务端在提交时用它在
/// **当时的账**上重新核对——前端不做规则判断，服务端仍是唯一权威（web/AGENTS.md §4）。
/// </para>
/// <para>
/// 两条"防呆"（同一条事实写两遍、两条互为反面）是平台可用性护栏，不是规则断言：
/// 说书人若确实想给重复内容，走自由文本兜底（R-0057 第 3 条仍然受理）。
/// </para>
/// </remarks>
internal static class SavantFactCombination
{
    /// <summary>按本次声明的真值组合核对两条候选。</summary>
    internal static SavantCombinationVerdict Check(
        TruthCombinationRule rule,
        SavantFactCandidate first,
        SavantFactCandidate second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Code == second.Code)
        {
            // C4：同一条事实写两遍（必然同真同假）；或互为反面（奇 / 偶一类，必然一真一假）。
            if (first.Parameter == second.Parameter)
            {
                return Reject("平台防呆：两条不能是同一条事实（要给重复内容请走自由文本兜底）");
            }

            if (first.OppositeGroup is not null)
            {
                return Reject(
                    $"平台防呆：「{first.Text}」与「{second.Text}」互为反面，必然一真一假——"
                    + "等于只给了一条信息（要这么给请走自由文本兜底）");
            }

            return Allow();
        }

        return rule switch
        {
            // C1：能力生效时两条必须一真一假（百科《博学者》· 角色能力；R-0057）。
            TruthCombinationRule.ExactlyOneTrue => first.Truth != second.Truth
                ? Allow()
                : Reject(
                    $"能力生效时必须一真一假：现在两条都为{(first.Truth == OptionTruth.True ? "真" : "假")}"
                    + "（百科《博学者》· 2026-10-01 抓取 · 角色能力；R-0057）"),

            // C3：涡流在场时两条都必须为假——哪怕博学者醉酒或中毒（R-0028）。
            TruthCombinationRule.AllFalse =>
                first.Truth == OptionTruth.False && second.Truth == OptionTruth.False
                    ? Allow()
                    : Reject("涡流在场：两条都必须为假，哪怕博学者醉酒或中毒（百科《涡流》· 2026-10-01 抓取；R-0028）"),

            // C2：能力未生效 → 任意组合（双真 / 双假 / 一真一假都可以，R-0057 第 5 条）；
            // Indeterminate / Unspecified → 平台不拦，提交时按当时的账再判（不猜，D-0015）。
            _ => Allow(),
        };
    }

    private static SavantCombinationVerdict Allow() => new() { Allowed = true };

    private static SavantCombinationVerdict Reject(string reason) => new() { Allowed = false, Reason = reason };
}
