namespace OpenClockTower.Server;

/// <summary>快照表行：当前步骤机状态的 JSON（派生数据，可随时从事件重算）。</summary>
public sealed class SnapshotEntity
{
    /// <summary>游戏标识（主键）。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>快照对应的序号。</summary>
    public long Sequence { get; set; }

    /// <summary>步骤机状态 JSON；尚未开阶段时为 null。</summary>
    public string? MachineJson { get; set; }

    /// <summary>快照生成时刻。</summary>
    public DateTimeOffset RecordedAt { get; set; }
}
