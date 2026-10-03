namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提出一个是 / 否问题：问题进事件流，只说书人与本人可见（R-0040）。
/// </summary>
public sealed record ArtistQuestionAskedEvent : GameEvent
{
    /// <summary>提问的艺术家席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>提问时刻该席位的角色（结清时的来源检索键）。</summary>
    public required CharacterId Character { get; init; }

    /// <summary>问题全文。</summary>
    public required string Question { get; init; }
}
