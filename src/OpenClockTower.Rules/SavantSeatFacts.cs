using System.Globalization;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实库 A 组「座位关系」：邪恶怎么坐（R-0057-C）。
/// </summary>
/// <remarks>
/// 来源：百科《博学者》· 2026-10-01 抓取 · 角色简介 2——「每个白天，说书人选择两条信息提供给博学者。
/// 一条必须是正确的，一条必须是错误的」。本组事实都是**平台能按账判定真假**的一句人话；
/// 挑哪两条、给不给，仍由说书人裁量（D-0002：平台不替说书人编内容）。
/// </remarks>
internal static class SavantSeatFacts
{
    /// <summary>分组名（说书人端分栏）。</summary>
    internal const string Group = "座位关系";

    /// <summary>本组全部事实（顺序 = 候选顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> All { get; } =
    [
        new()
        {
            Code = "demon-seat-parity",
            Group = Group,
            ExclusionGroup = "demon-seat-parity",
            Parameters = _ => ["odd", "even"],
            Evaluate = (world, parameter) =>
                parameter is not ("odd" or "even") || world.SingleDemon is not { } demon
                    ? null
                    : SavantFactEvaluation.Of(
                        (demon.Value % 2 == 1) == (parameter == "odd"),
                        parameter == "odd" ? "恶魔坐在奇数位" : "恶魔坐在偶数位"),
        },
        new()
        {
            Code = "demon-minion-gap",
            Group = Group,
            Parameters = world =>
            [
                .. Enumerable.Range(0, world.MaxGap + 1)
                    .Select(gap => gap.ToString(CultureInfo.InvariantCulture)),
            ],
            Evaluate = (world, parameter) =>
                TryGap(parameter, out var gap) && NearestMinionGap(world) is { } actual
                    ? SavantFactEvaluation.Of(
                        actual == gap,
                        gap == 0 ? "恶魔与最近的爪牙相邻" : $"恶魔与最近的爪牙之间隔着 {gap} 名玩家")
                    : null,
        },
        new()
        {
            Code = "minion-beside-demon",
            Group = Group,
            Evaluate = (world, _) => NeighboursOfDemon(world) is { } neighbours
                && neighbours.All(seat => world.CharacterOf(seat) is not null)
                    ? SavantFactEvaluation.Of(
                        neighbours.Any(seat => world.TypeOf(seat) == CharacterType.Minion),
                        "恶魔左右相邻的席位里有爪牙")
                    : null,
        },
        new()
        {
            Code = "minions-adjacent",
            Group = Group,
            Evaluate = (world, _) => !world.AllCharactersKnown
                ? null
                : SavantFactEvaluation.Of(
                    AnyAdjacent(world, world.SeatsOfType(CharacterType.Minion)),
                    "两名爪牙彼此相邻"),
        },
        new()
        {
            Code = "demon-beside-outsider",
            Group = Group,
            Evaluate = (world, _) => NeighboursOfDemon(world) is { } neighbours
                && neighbours.All(seat => world.CharacterOf(seat) is not null)
                    ? SavantFactEvaluation.Of(
                        neighbours.Any(seat => world.TypeOf(seat) == CharacterType.Outsider),
                        "恶魔左右相邻的席位里有外来者")
                    : null,
        },
        new()
        {
            Code = "demon-neighbours-team",
            Group = Group,
            ExclusionGroup = "demon-neighbours-team",
            Parameters = _ => ["good", "evil"],
            Evaluate = (world, parameter) => parameter is not ("good" or "evil")
                ? null
                : NeighboursOfDemon(world) is { } neighbours
                    && neighbours.All(seat => world.AlignmentOf(seat) is not null)
                        ? SavantFactEvaluation.Of(
                            neighbours.All(seat =>
                                world.AlignmentOf(seat) == (parameter == "good" ? Alignment.Good : Alignment.Evil)),
                            parameter == "good"
                                ? "恶魔左右相邻的席位都是善良阵营"
                                : "恶魔左右相邻的席位都是邪恶阵营")
                        : null,
        },
    ];

    /// <summary>恶魔的相邻席位；恶魔不唯一（麻脸巫婆造过第二个恶魔）或圆桌上没有相邻席位时返回 null。</summary>
    private static IReadOnlyList<SeatId>? NeighboursOfDemon(SavantFactWorld world)
    {
        if (world.SingleDemon is not { } demon)
        {
            return null;
        }

        var neighbours = world.NeighboursOf(demon);
        return neighbours.Count == 0 ? null : neighbours;
    }

    /// <summary>
    /// 恶魔与**最近的**爪牙之间隔着几名玩家；恶魔不唯一、角色没观测齐、或场上没有爪牙时返回 null
    /// （说不清"最近"就不说）。
    /// </summary>
    private static int? NearestMinionGap(SavantFactWorld world)
    {
        if (!world.AllCharactersKnown || world.SingleDemon is not { } demon)
        {
            return null;
        }

        var minions = world.SeatsOfType(CharacterType.Minion);
        if (minions.Count == 0)
        {
            return null;
        }

        var gaps = minions.Select(minion => world.GapBetween(demon, minion)).ToList();
        return gaps.Any(gap => gap is null) ? null : gaps.Min();
    }

    /// <summary>这一组席位里有没有任意两名是相邻的（两两比较，圆桌上相邻 = 隔着 0 名玩家）。</summary>
    private static bool AnyAdjacent(SavantFactWorld world, IReadOnlyList<SeatId> seats)
    {
        for (var left = 0; left < seats.Count; left++)
        {
            for (var right = left + 1; right < seats.Count; right++)
            {
                if (world.GapBetween(seats[left], seats[right]) == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>解析 gap 参数：必须是非负整数（客户端塞了别的取值 = 判不了）。</summary>
    private static bool TryGap(string? parameter, out int gap) =>
        int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out gap) && gap >= 0;
}
