namespace OpenClockTower.Server;

/// <summary>事件表行：序号单调递增，载荷是 JSON（唯一事实来源，D-0010）。</summary>
public sealed class EventEntity
{
    /// <summary>游戏标识（复合主键的一部分）。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>事件序号（复合主键的一部分）。</summary>
    public long Sequence { get; set; }

    /// <summary>事件类型名。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>事件载荷 JSON。</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>宿主记录的发生时刻。</summary>
    public DateTimeOffset RecordedAt { get; set; }
}
