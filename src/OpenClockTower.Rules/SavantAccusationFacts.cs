using System.Globalization;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实库 E 组「点名」：直接点到席位或角色的高强度信息（R-0057-C）。
/// </summary>
/// <remarks>
/// <para>
/// 这一组把"某某是谁"这类信息摆到说书人面前，是**最强也最容易毁局**的一类：平台只把它标成
/// 「高强度」并算清真值，用不用、给不给，仍然由说书人裁量（票据「残余 / 风险」：强度平衡不属于平台职责）。
/// </para>
/// <para>
/// 参数空间刻意有界：`seat-character` 只枚举**本局在场角色**（席位 × 在场角色）；
/// 想报一个不在场的角色名，走 `role-in-play` 或自由文本兜底（不猜，也不把 30 × 席位全铺开）。
/// </para>
/// </remarks>
internal static class SavantAccusationFacts
{
    /// <summary>分组名（说书人端分栏）。</summary>
    internal const string Group = "点名";

    /// <summary>点名类的高强度徽章文案。</summary>
    internal const string HighIntensityTag = "高强度";

    /// <summary>本组全部事实（顺序 = 候选顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> All { get; } =
    [
        new()
        {
            Code = "seat-is-evil",
            Group = Group,
            HighIntensity = true,
            Parameters = SeatValues,
            Evaluate = (world, parameter) => SeatOf(world, parameter) is { } seat
                ? world.AlignmentOf(seat) is { } alignment
                    ? SavantFactEvaluation.Of(alignment == Alignment.Evil, $"{seat.Value} 号玩家是邪恶阵营")
                    : null
                : null,
        },
        new()
        {
            Code = "two-seats-same-team",
            Group = Group,
            HighIntensity = true,
            Parameters = SeatPairs,
            Evaluate = (world, parameter) =>
            {
                var (left, right) = ParsePair(world, parameter);
                if (left is not { } first || right is not { } second)
                {
                    return null;
                }

                return world.AlignmentOf(first) is { } firstAlignment
                    && world.AlignmentOf(second) is { } secondAlignment
                        ? SavantFactEvaluation.Of(
                            firstAlignment == secondAlignment,
                            $"{first.Value} 号与 {second.Value} 号同阵营")
                        : null;
            },
        },
        new()
        {
            Code = "role-in-play",
            Group = Group,
            HighIntensity = true,
            Parameters = _ => [.. SectsAndVioletsRoster.All.Select(character => character.Value)],
            Evaluate = (world, parameter) => CharacterOf(parameter) is { } character
                ? AnySeatWith(world, character, life: null) is { } inPlay
                    ? SavantFactEvaluation.Of(inPlay, $"角色「{DisplayNameOf(character)}」在场")
                    : null
                : null,
        },
        new()
        {
            Code = "role-dead",
            Group = Group,
            HighIntensity = true,
            Parameters = _ => [.. SectsAndVioletsRoster.All.Select(character => character.Value)],
            Evaluate = (world, parameter) => CharacterOf(parameter) is { } character
                ? AnySeatWith(world, character, LifeState.Dead) is { } dead
                    ? SavantFactEvaluation.Of(dead, $"角色「{DisplayNameOf(character)}」已经死亡")
                    : null
                : null,
        },
        new()
        {
            Code = "seat-character",
            Group = Group,
            HighIntensity = true,
            Parameters = SeatCharacterPairs,
            Evaluate = (world, parameter) =>
            {
                var (seat, character) = ParseSeatCharacter(world, parameter);
                if (seat is not { } target || character is not { } claimed)
                {
                    return null;
                }

                return world.CharacterOf(target) is { } actual
                    ? SavantFactEvaluation.Of(
                        actual == claimed,
                        $"{target.Value} 号玩家的角色是「{DisplayNameOf(claimed)}」")
                    : null;
            },
        },
    ];

    /// <summary>候选里的席位参数（在局席位，按座位号升序）。</summary>
    private static IReadOnlyList<string> SeatValues(SavantFactWorld world) =>
        [.. world.Circle.Select(seat => seat.Value.ToString(CultureInfo.InvariantCulture))];

    /// <summary>候选里的（席位 × 席位）参数：只取座位号小的在前，避免同一对出现两次。</summary>
    private static IReadOnlyList<string> SeatPairs(SavantFactWorld world)
    {
        var seats = world.Circle;
        var pairs = new List<string>();
        for (var left = 0; left < seats.Count; left++)
        {
            for (var right = left + 1; right < seats.Count; right++)
            {
                pairs.Add(
                    $"{seats[left].Value.ToString(CultureInfo.InvariantCulture)}:"
                    + seats[right].Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        return pairs;
    }

    /// <summary>候选里的（席位 × 在场角色）参数：角色按花名册顺序，席位按座位号升序。</summary>
    private static IReadOnlyList<string> SeatCharacterPairs(SavantFactWorld world)
    {
        var inPlay = InPlayCharacters(world);
        var pairs = new List<string>();
        foreach (var seat in world.Circle)
        {
            foreach (var character in inPlay)
            {
                pairs.Add($"{seat.Value.ToString(CultureInfo.InvariantCulture)}:{character.Value}");
            }
        }

        return pairs;
    }

    /// <summary>本局在場的角色（去重，按花名册顺序）——角色未观测齐时只给已观测到的部分。</summary>
    private static IReadOnlyList<CharacterId> InPlayCharacters(SavantFactWorld world)
    {
        var assigned = world.Circle
            .Select(seat => world.CharacterOf(seat))
            .OfType<CharacterId>()
            .ToHashSet();

        return [.. SectsAndVioletsRoster.All.Where(assigned.Contains)];
    }

    /// <summary>某个角色是不是（活着 / 死了 / 不管生死地）坐在某个在局席位上。</summary>
    /// <remarks>
    /// 返回 null 表示**说不了假话**：要说"场上没有这个角色"或"这个角色没死"，必须所有在局席位的
    /// 角色（以及生死）都观测齐（不猜，D-0015）。
    /// </remarks>
    private static bool? AnySeatWith(SavantFactWorld world, CharacterId character, LifeState? life)
    {
        var seats = world.Circle.Where(seat => world.CharacterOf(seat) == character).ToList();
        if (life is { } required)
        {
            if (seats.Any(seat => world.LifeOf(seat) == required))
            {
                return true;
            }

            return world.AllCharactersKnown && world.AllLivesKnown ? false : null;
        }

        return seats.Count > 0 ? true : world.AllCharactersKnown ? false : null;
    }

    /// <summary>解析单席位参数。</summary>
    private static SeatId? SeatOf(SavantFactWorld world, string? parameter)
    {
        if (!int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        foreach (var seat in world.Circle)
        {
            if (seat.Value == value)
            {
                return seat;
            }
        }

        return null;
    }

    /// <summary>解析「席位:席位」参数；两个席位都必须解析成功（想给一个就给不了）。</summary>
    private static (SeatId? Left, SeatId? Right) ParsePair(SavantFactWorld world, string? parameter)
    {
        if (parameter is null)
        {
            return (null, null);
        }

        var separator = parameter.IndexOf(':', StringComparison.Ordinal);
        return separator <= 0
            ? (null, null)
            : (SeatOf(world, parameter[..separator]), SeatOf(world, parameter[(separator + 1)..]));
    }

    /// <summary>解析「席位:角色」参数。</summary>
    private static (SeatId? Seat, CharacterId? Character) ParseSeatCharacter(SavantFactWorld world, string? parameter)
    {
        if (parameter is null)
        {
            return (null, null);
        }

        var separator = parameter.IndexOf(':', StringComparison.Ordinal);
        return separator <= 0
            ? (null, null)
            : (SeatOf(world, parameter[..separator]), CharacterOf(parameter[(separator + 1)..]));
    }

    /// <summary>花名册里的角色；不在册（客户端塞了别的 slug）返回 null。</summary>
    private static CharacterId? CharacterOf(string? slug)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        var character = new CharacterId(slug);
        return SectsAndVioletsRoster.Contains(character) ? character : null;
    }

    private static string DisplayNameOf(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;
}
