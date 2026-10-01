using System.Globalization;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 行动契约里「选一个席位」的选项值格式：<c>seat:3</c>。
/// </summary>
/// <remarks>
/// 选项值要能进事件流、能被重放与校验，必须是稳定、人可读的文本；格式与解析收在一处，
/// 免得每个契约各写一份——写歪了就是「合法性闸放行、结算时找不到目标」。
/// </remarks>
internal static class SeatChoice
{
    private const string Prefix = "seat:";

    /// <summary>把一个席位编码成选项值。</summary>
    internal static string Format(SeatId seat) =>
        string.Concat(Prefix, seat.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>解析选项值；不是席位选择时返回 null。</summary>
    internal static SeatId? Parse(string? value)
    {
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(
            value.AsSpan(Prefix.Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var seat)
            && seat > 0
                ? new SeatId(seat)
                : null;
    }
}
