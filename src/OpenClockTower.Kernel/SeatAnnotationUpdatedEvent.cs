namespace OpenClockTower.Kernel;

/// <summary>说书人改了一条注记的文本（D-0019）；席位与标识不变，折叠时原地更新。</summary>
public sealed record SeatAnnotationUpdatedEvent : GameEvent
{
    /// <summary>更新后的注记（标识不变、文本已归一化）。</summary>
    public required SeatAnnotation Annotation { get; init; }
}
