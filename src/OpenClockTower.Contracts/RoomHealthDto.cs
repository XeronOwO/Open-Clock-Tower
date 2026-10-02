namespace OpenClockTower.Contracts;

/// <summary>房间健康位：恢复 / 重建失败后为降级态（原因 + 发生时间）；正常时 Degraded=false。</summary>
public sealed record RoomHealthDto
{
    /// <summary>是否处于降级（数据可能已丢失）。</summary>
    public required bool Degraded { get; init; }

    /// <summary>降级原因（人话，含失败动作）；正常时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>首次降级时刻；正常时为 null。</summary>
    public DateTimeOffset? Since { get; init; }
}
