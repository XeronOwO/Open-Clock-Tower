namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提问结清：清空进行中问题，并留下结清方式（回答 / 要求重问 / 强推作废）。
/// </summary>
public sealed record ArtistQuestionClosedEvent : GameEvent
{
    /// <summary>提问的艺术家席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>结清方式（R-0040）。</summary>
    public required ArtistQuestionClosure Closure { get; init; }
}
