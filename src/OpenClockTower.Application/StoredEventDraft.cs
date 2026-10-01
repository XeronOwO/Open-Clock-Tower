using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>待追加的一条事件：序号已由应用层分配好，等待一次原子提交。</summary>
public sealed record StoredEventDraft
{
    /// <summary>分配好的事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>领域事件。</summary>
    public required GameEvent Event { get; init; }

    /// <summary>本次提交统一记录的时刻。</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
