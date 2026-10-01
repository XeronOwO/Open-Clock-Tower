using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>事件流里的一条事件：序号单调递增，时间戳由宿主在追加时记录（D-0008：内核无时间）。</summary>
public sealed record StoredEvent
{
    /// <summary>单调递增的事件序号（从 1 开始）。</summary>
    public required long Sequence { get; init; }

    /// <summary>领域事件。</summary>
    public required GameEvent Event { get; init; }

    /// <summary>宿主记录的发生时刻。</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
