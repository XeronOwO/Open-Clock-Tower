namespace OpenClockTower.Server;

/// <summary>幂等回执表行：同一幂等键只允许一条，重复投递据此重放首次结果。</summary>
public sealed class ReceiptEntity
{
    /// <summary>游戏标识（复合主键的一部分）。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>幂等键（复合主键的一部分）。</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>首次产出事件的首序号。</summary>
    public long FirstSequence { get; set; }

    /// <summary>首次产出事件的末序号。</summary>
    public long LastSequence { get; set; }
}
