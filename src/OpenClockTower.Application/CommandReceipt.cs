namespace OpenClockTower.Application;

/// <summary>
/// 一次已接受命令的回执：重复投递时按它重放首次结果（幂等闸）。
/// </summary>
public sealed record CommandReceipt
{
    /// <summary>幂等键。</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>首次产出事件的首序号。</summary>
    public required long FirstSequence { get; init; }

    /// <summary>首次产出事件的末序号；没有产出事件时小于首序号。</summary>
    public required long LastSequence { get; init; }

    /// <summary>本次提交是否没有产出任何事件。</summary>
    public bool IsEmpty => LastSequence < FirstSequence;
}
