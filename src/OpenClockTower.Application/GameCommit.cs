namespace OpenClockTower.Application;

/// <summary>
/// 一次原子提交：事件 + 快照 + 回执必须同生共死。
/// </summary>
/// <remarks>
/// 半提交（事件落了、回执没落）会破坏幂等；因此持久化实现必须在一个事务里写它们。
/// </remarks>
public sealed record GameCommit
{
    /// <summary>目标游戏。</summary>
    public required GameId GameId { get; init; }

    /// <summary>本次追加的事件。</summary>
    public required IReadOnlyList<StoredEventDraft> Events { get; init; }

    /// <summary>提交后的状态快照。</summary>
    public required StoredSnapshot Snapshot { get; init; }

    /// <summary>命令回执；系统命令与重建命令也可以有。</summary>
    public CommandReceipt? Receipt { get; init; }
}
