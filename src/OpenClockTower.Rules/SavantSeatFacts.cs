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
            ExclusiveValues = true,
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
            // 单位与钟表匠**同一套**（百科《钟表匠》· 2026-10-01 抓取 · 规则细节 3：「距离值等同于：
            // 恶魔与爪牙这两名玩家之间的玩家数量 + 1。因此，钟表匠能得知的最小数字为『1』」）：
            // 相邻 = 距离 1。同一局面下两处读数必须一致，否则说书人手里会出现两套刻度。
            Code = "demon-minion-distance",
            Group = Group,

            // 与「旁边有爪牙」是同一个事实的两种说法（距离 1 当且仅当恶魔的某个相邻席位是爪牙）：
            // **只有距离 1 那个取值**与那条互斥（距离 2 / 3 是别的事），所以按取值给互斥组——
            // 同组 ⇒ 说书人端互相灰掉，服务端在双真时也拒（R-0057-C 的 C1）。
            ExclusionGroupOf = (_, parameter) => parameter == "1" ? "demon-minion-adjacency" : null,
            Parameters = world =>
            [
                .. Enumerable.Range(1, world.MaxDistance)
                    .Select(distance => distance.ToString(CultureInfo.InvariantCulture)),
            ],
            Evaluate = (world, parameter) =>
                TryDistance(parameter, out var distance) && NearestMinionDistance(world) is { } actual
                    ? SavantFactEvaluation.Of(
                        actual == distance,
                        distance == 1 ? "恶魔与最近的爪牙相邻（距离 1）" : $"恶魔与最近的爪牙相距 {distance}")
                    : null,
        },
        new()
        {
            // 与上面那条距离读数是**同一个事实**：区别只在观测闸门——这条只要恶魔左右两个邻居的
            // 角色已知就说得出口（全场角色没观测齐时仍然可用），那条要求全场角色已知。
            Code = "minion-beside-demon",
            Group = Group,
            ExclusionGroup = "demon-minion-adjacency",
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
            // 「旁边有谁」合并成一条带类型参数的：爪牙 / 外来者 / 镇民三句话是同一段判断，
            // 拆成两条等于同一段代码写两遍（镇民那一条原本根本给不出来）。
            // 恶魔不唯一 / 邻居角色没观测齐时整条判不了（不猜，D-0015）。
            Code = "demon-neighbour-type",
            Group = Group,
            Parameters = _ => ["minion", "outsider", "townsfolk"],
            Evaluate = (world, parameter) => NeighbourType(parameter) is not { } type
                ? null
                : NeighboursOfDemon(world) is { } neighbours
                    && neighbours.All(seat => world.CharacterOf(seat) is not null)
                        ? SavantFactEvaluation.Of(
                            neighbours.Any(seat => world.TypeOf(seat) == type),
                            $"恶魔左右相邻的席位里有{TypeText(parameter)}")
                        : null,
        },
        new()
        {
            Code = "demon-neighbours-team",
            Group = Group,
            ExclusiveValues = true,
            Parameters = _ => ["good", "evil", "mixed"],
            Evaluate = (world, parameter) => parameter is not ("good" or "evil" or "mixed")
                ? null
                : NeighboursOfDemon(world) is { } neighbours
                    && neighbours.All(seat => world.AlignmentOf(seat) is not null)
                        ? SavantFactEvaluation.Of(
                            parameter switch
                            {
                                "good" => neighbours.All(seat => world.AlignmentOf(seat) == Alignment.Good),
                                "evil" => neighbours.All(seat => world.AlignmentOf(seat) == Alignment.Evil),
                                _ => neighbours.Any(seat => world.AlignmentOf(seat) == Alignment.Good)
                                    && neighbours.Any(seat => world.AlignmentOf(seat) == Alignment.Evil),
                            },
                            parameter switch
                            {
                                "good" => "恶魔左右相邻的席位都是善良阵营",
                                "evil" => "恶魔左右相邻的席位都是邪恶阵营",
                                _ => "恶魔左右相邻的席位一善一恶",
                            })
                        : null,
        },
    ];

    /// <summary>「旁边有谁」的类型参数 → 角色类型；册外取值返回 null（判不了）。</summary>
    private static CharacterType? NeighbourType(string? parameter) => parameter switch
    {
        "minion" => CharacterType.Minion,
        "outsider" => CharacterType.Outsider,
        "townsfolk" => CharacterType.Townsfolk,
        _ => null,
    };

    /// <summary>「旁边有谁」的类型参数 → 人话里的类型名。</summary>
    private static string TypeText(string? parameter) => parameter switch
    {
        "minion" => "爪牙",
        "outsider" => "外来者",
        _ => "镇民",
    };

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
    /// 恶魔与**最近的**爪牙之间的**距离**（钟表匠口径 = 隔着的人数 + 1，相邻 = 1）；
    /// 恶魔不唯一、角色没观测齐、或场上没有爪牙时返回 null（说不清"最近"就不说）。
    /// </summary>
    private static int? NearestMinionDistance(SavantFactWorld world)
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
        return gaps.Any(gap => gap is null) ? null : gaps.Min() + 1;
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

    /// <summary>
    /// 解析距离参数：必须是**正整数**（钟表匠口径的最小值是 1；客户端塞 0 或别的取值 = 判不了）。
    /// </summary>
    private static bool TryDistance(string? parameter, out int distance) =>
        int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out distance) && distance >= 1;
}
