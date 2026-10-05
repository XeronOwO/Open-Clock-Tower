namespace OpenClockTower.Kernel;

/// <summary>杂耍艺人公开做出了他的猜测（含 0 条：公开声明但不猜）。</summary>
/// <remarks>
/// 这条事件同时说明两件事：**猜测是公开的**（进白天公开面，所有玩家可见），
/// 以及**这次持有的能力已经用掉**（每个首个白天只有一次公开猜测，见 R-0057-B）。
/// 猜对数不在这里——当晚由说书人给出，只到本人。
/// </remarks>
public sealed record JugglerGuessesMadeEvent : GameEvent
{
    /// <summary>猜测者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>发生在第几个白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>猜测内容（0–5 条，按提交顺序）。</summary>
    public required IReadOnlyList<JugglerGuess> Guesses { get; init; }
}
