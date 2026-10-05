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

    /// <summary>`type-count-equals` 的取值维度：四种可数类型。</summary>
    private static readonly (string Key, CharacterType Type, string Text)[] CountableTypes =
    [
        ("townsfolk", CharacterType.Townsfolk, "镇民"),
        ("outsider", CharacterType.Outsider, "外来者"),
        ("minion", CharacterType.Minion, "爪牙"),
        ("demon", CharacterType.Demon, "恶魔"),
    ];

    /// <summary>本组全部事实（顺序 = 候选顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> All { get; } =
    [
        new()
        {
            // 三态合成一条：这三句话互斥且穷尽（邪恶多 / 平 / 善良多），拆成三条会给出"同一件事的两种说法"。
            Code = "alive-lead",
            Group = Group,
            ExclusiveValues = true,
            Parameters = _ => ["evil", "tied", "good"],
            Evaluate = (world, parameter) => parameter is not ("evil" or "tied" or "good")
                || !TryCounts(world, out var good, out var evil)
                    ? null
                    : SavantFactEvaluation.Of(
                        parameter switch
                        {
                            "evil" => evil > good,
                            "tied" => evil == good,
                            _ => good > evil,
                        },
                        parameter switch
                        {
                            "evil" => "邪恶阵营的存活人数多于善良阵营",
                            "tied" => "善良与邪恶的存活人数相同",
                            _ => "善良阵营的存活人数多于邪恶阵营",
                        }),
        },
        new()
        {
            Code = "alive-count-parity",
            Group = Group,
            ExclusiveValues = true,
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
            ExclusiveValues = true,
            Parameters = world => Counts(world),
            Evaluate = (world, parameter) => TryCount(parameter, out var count)
                && TryCounts(world, out var good, out var evil)
                    ? SavantFactEvaluation.Of(good + evil == count, $"存活人数恰好是 {count}")
                    : null,
        },
        new()
        {
            // 善良比邪恶多 N 名。N = 0（两边一样多）由 alive-lead:tied 表达，负值由 alive-lead:evil 表达，
            // 因此取值从 1 起；上限 = 圆桌人数 − 1（邪恶至少有一名恶魔）。
            Code = "good-lead",
            Group = Group,
            ExclusiveValues = true,
            Parameters = world =>
            [
                .. Enumerable.Range(1, Math.Max(0, world.Circle.Count - 1))
                    .Select(lead => lead.ToString(CultureInfo.InvariantCulture)),
            ],
            Evaluate = (world, parameter) => TryCount(parameter, out var lead) && lead >= 1
                && TryCounts(world, out var good, out var evil)
                    ? SavantFactEvaluation.Of(
                        good - evil == lead,
                        $"善良阵营比邪恶阵营多 {lead} 名存活玩家")
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
        new()
        {
            // 「只有一名外来者在场」这类读数（百科《博学者》· 范例 4）：按角色类型数在局席位。
            // 只数**已观测到类型**的席位——没观测齐时"恰好 N 名"可能变成"至少 N 名"，
            // 因此要求所有在局席位的角色都已知（否则整条判不了，不猜，D-0015）。
            Code = "type-count-equals",
            Group = Group,
            Parameters = world =>
            [
                .. from type in CountableTypes
                   from count in Counts(world)
                   select $"{type.Key}:{count}",
            ],
            Evaluate = (world, parameter) =>
            {
                if (!world.AllCharactersKnown || ParseCount(parameter) is not { } parsed)
                {
                    return null;
                }

                var type = CountableTypes.FirstOrDefault(candidate => candidate.Key == parsed.TypeKey);
                return type == default
                    ? null
                    : SavantFactEvaluation.Of(
                        world.SeatsOfType(type.Type).Count == parsed.Count,
                        $"场上有 {parsed.Count} 名{type.Text}");
            },
        },
    ];

    /// <summary>解析 <c>类型:人数</c> 参数；形状不对或人数不是非负整数时返回 null。</summary>
    private static (string TypeKey, int Count)? ParseCount(string? parameter)
    {
        if (parameter is null)
        {
            return null;
        }

        var separator = parameter.IndexOf(':', StringComparison.Ordinal);
        return separator <= 0 || !TryCount(parameter[(separator + 1)..], out var count)
            ? null
            : (parameter[..separator], count);
    }

    /// <summary>存活人数的候选取值（1 .. 圆桌人数）：账上恶魔在场 ⇒ 存活数不可能是 0。</summary>
    private static IReadOnlyList<string> Counts(SavantFactWorld world) =>
    [
        .. Enumerable.Range(1, Math.Max(0, world.Circle.Count))
            .Select(count => count.ToString(CultureInfo.InvariantCulture)),
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
