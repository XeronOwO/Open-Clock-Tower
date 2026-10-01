namespace OpenClockTower.Contracts;

/// <summary>说书人"卡点"摘要：谁在卡着、卡在哪一步、卡了多久。</summary>
public sealed record PendingRequestDto
{
    /// <summary>被卡住的玩家席位。</summary>
    public required int Seat { get; init; }

    /// <summary>请求标识。</summary>
    public required string RequestId { get; init; }

    /// <summary>槽位标识。</summary>
    public required string SlotId { get; init; }

    /// <summary>槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>已等待秒数；时间线未知时为 null。</summary>
    public double? WaitingSeconds { get; init; }
}
