using System.Globalization;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实库 B 组「阵营与人数」：局势读数（R-0057-C）。
/// </summary>
/// <remarks>
/// 这一组全部按**存活席位**算，且要求生死与阵营两个维度都观测齐——人数类事实一旦有一个维度
/// 没观测齐，就既不能说真也不能说假（判不了 → 不进候选，D-0015）。
/// </remarks>
internal static class SavantNumberFacts
{
    /// <summary>分组名（说书人端分栏）。</summary>
    internal const string Group = "阵营与人数";

    /// <summary>本组全部事实（顺序 = 候选顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> All { get; } =
    [
        new()
        {
            Code = "evil-not-fewer",
            Group = Group,
            Evaluate = (world, _) => TryCounts(world, out var good, out var evil)
                ? SavantFactEvaluation.Of(evil >= good, "邪恶阵营的存活人数不少于善良阵营")
                : null,
        },
        new()
        {
            Code = "evil-majority",
            Group = Group,
            Evaluate = (world, _) => TryCounts(world, out var good, out var evil)
                ? SavantFactEvaluation.Of(evil > good, "邪恶阵营的存活人数多于善良阵营")
                : null,
        },
        new()
        {
            Code = "alive-count-parity",
            Group = Group,
            ExclusionGroup = "alive-count-parity",
            Parameters = _ => ["odd", "even"],
            Evaluate = (world, parameter) => parameter is not ("odd" or "even")
                || !TryCounts(world, out var good, out var evil)
                    ? null
                    : SavantFactEvaluation.Of(
                        ((good + evil) % 2 == 1) == (parameter == "odd"),
                        parameter == "odd" ? "存活人数是奇数" : "存活人数是偶数"),
        },
        new()
        {
            Code = "alive-count-equals",
            Group = Group,
            Parameters = world => Counts(world),
            Evaluate = (world, parameter) => TryCount(parameter, out var count)
                && TryCounts(world, out var good, out var evil)
                    ? SavantFactEvaluation.Of(good + evil == count, $"存活人数恰好是 {count}")
                    : null,
        },
        new()
        {
            Code = "good-lead",
            Group = Group,
            Parameters = world => Counts(world),
            Evaluate = (world, parameter) => TryCount(parameter, out var lead)
                && TryCounts(world, out var good, out var evil)
                    ? SavantFactEvaluation.Of(
                        good - evil == lead,
                        lead == 0 ? "善良与邪恶的存活人数相同" : $"善良阵营比邪恶阵营多 {lead} 名存活玩家")
                    : null,
        },
        new()
        {
            Code = "traveller-present",
            Group = Group,
            Evaluate = (world, _) => !world.AllCharactersKnown
                ? null
                : SavantFactEvaluation.Of(
                    world.SeatsOfType(CharacterType.Traveller).Count > 0,
                    "场上有旅行者"),
        },
    ];

    /// <summary>存活人数的候选取值（0 .. 圆桌人数）。</summary>
    private static IReadOnlyList<string> Counts(SavantFactWorld world) =>
    [
        .. Enumerable.Range(0, world.Circle.Count + 1).Select(count => count.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>解析人数参数：必须是非负整数。</summary>
    private static bool TryCount(string? parameter, out int count) =>
        int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= 0;

    /// <summary>两个阵营的存活人数；生死或阵营没观测齐时返回 false（判不了）。</summary>
    private static bool TryCounts(SavantFactWorld world, out int good, out int evil)
    {
        good = 0;
        evil = 0;

        if (!world.AllLivesKnown || !world.AllAlignmentsKnown)
        {
            return false;
        }

        good = world.AliveOf(Alignment.Good).Count;
        evil = world.AliveOf(Alignment.Evil).Count;
        return true;
    }
}
