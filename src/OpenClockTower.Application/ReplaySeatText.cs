using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 复盘文案的席位文本口径（D-0021）：有玩家名写「N 号 · 玩家名」，没有名字回退「N 号」。
/// </summary>
/// <remarks>
/// <para>
/// 同一份口径同时用于复盘步骤文案与选项值里的席位分量；名字来自会话读模型（<see cref="SeatNameDirectory"/>），
/// 是**读取时**的当前玩家名（D-0021 的明账：不做逐步骤历史名）。
/// </para>
/// <para>
/// 这是实例而不是静态工具：一次复盘投影带着自己那份名册快照，避免"把当前名册塞进全局状态"竞态。
/// </para>
/// </remarks>
public sealed class ReplaySeatText
{
    private readonly IReadOnlyDictionary<SeatId, string> _names;

    /// <summary>用一份「席位 → 玩家名」快照构造；空 / null = 全部回退席位号。</summary>
    public ReplaySeatText(IReadOnlyList<SeatDisplayName>? seatNames = null)
    {
        var names = new Dictionary<SeatId, string>();
        foreach (var item in seatNames ?? [])
        {
            if (!string.IsNullOrEmpty(item.DisplayName))
            {
                names[item.Seat] = item.DisplayName;
            }
        }

        _names = names;
    }

    /// <summary>席位文本；未知席位（null）写作「（未知席位）」，与既有口径一致。</summary>
    public string Seat(SeatId? seat) => seat is { } value ? Format(value) : "（未知席位）";

    /// <summary>席位列表文本（顿号分隔）。</summary>
    public string SeatList(IEnumerable<SeatId> seats) => string.Join("、", seats.Select(seat => Format(seat)));

    /// <summary>
    /// 选项值里的席位分量（<c>seat:N</c> / <c>pair:A+B</c>）翻译成同一席位口径；
    /// 不是席位分量时返回 false，调用方按原口径处理。
    /// </summary>
    public bool TryFormatOptionSeatPart(string part, out string text)
    {
        text = string.Empty;

        if (part.StartsWith("seat:", StringComparison.Ordinal)
            && int.TryParse(part["seat:".Length..], out var seat))
        {
            text = Format(new SeatId(seat));
            return true;
        }

        if (part.StartsWith("pair:", StringComparison.Ordinal))
        {
            var seats = part["pair:".Length..]
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (seats.Length == 2
                && int.TryParse(seats[0], out var first)
                && int.TryParse(seats[1], out var second))
            {
                text = $"{Format(new SeatId(first))} + {Format(new SeatId(second))}";
                return true;
            }
        }

        return false;
    }

    private string Format(SeatId seat) =>
        _names.TryGetValue(seat, out var name) ? $"{seat.Value} 号 · {name}" : $"{seat.Value} 号";
}
