namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者提问结清：清空进行中提问，并留下结清方式（已回答 / 强推作废）。
/// </summary>
public sealed record SavantQuestionClosedEvent : GameEvent
{
    /// <summary>提问的博学者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>结清方式。</summary>
    public required SavantQuestionClosure Closure { get; init; }
}
