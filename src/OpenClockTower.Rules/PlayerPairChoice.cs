using System.Globalization;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 通用「两名玩家」原子选择的选项值格式：<c>pair:1+3</c>（升序规范化）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SeatChoice"/> 同族：选项值要能进事件流、能被重放与校验，格式与解析收在一处。
/// 理发师的角色交换与贤者的当晚展示都用它——写歪了就是「合法性闸放行、结算时解析不出来」。
/// </para>
/// <para>
/// 交换 / 展示无序，因此玩家对一律按席位号升序规范化（<c>pair:1+3</c> 与 <c>pair:3+1</c> 是同一条）。
/// </para>
/// </remarks>
internal static class PlayerPairChoice
{
    private const string PairPrefix = "pair:";
    private const char Separator = '+';

    /// <summary>把一对席位编码成选项值（升序规范化）。</summary>
    internal static string FormatPair(SeatId first, SeatId second)
    {
        var low = Math.Min(first.Value, second.Value);
        var high = Math.Max(first.Value, second.Value);
        return $"{PairPrefix}{low.ToString(CultureInfo.InvariantCulture)}"
            + $"{Separator}{high.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>解析玩家对；不是合法形状（含两个相同席位）时返回 null。</summary>
    internal static (SeatId First, SeatId Second)? ParsePair(string? value)
    {
        if (value is null || !value.StartsWith(PairPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = value.AsSpan(PairPrefix.Length);
        var separator = body.IndexOf(Separator);
        if (separator <= 0 || separator == body.Length - 1)
        {
            return null;
        }

        var firstText = body[..separator];
        var secondText = body[(separator + 1)..];
        return int.TryParse(firstText, NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            && int.TryParse(secondText, NumberStyles.None, CultureInfo.InvariantCulture, out var second)
            && first > 0
            && second > 0
            && first != second
                ? (new SeatId(Math.Min(first, second)), new SeatId(Math.Max(first, second)))
                : null;
    }
}
