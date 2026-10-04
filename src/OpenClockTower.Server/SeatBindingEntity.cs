namespace OpenClockTower.Server;

/// <summary>席位绑定表行（D-0021）：会话层的「席位 ↔ 账号」认领关系，不进事件流。</summary>
/// <remarks>两个唯一约束：<c>(GameId, Seat)</c> 一席一账号、<c>(GameId, AccountId)</c> 一账号一席。</remarks>
public sealed class SeatBindingEntity
{
    /// <summary>对局标识。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>席位号。</summary>
    public int Seat { get; set; }

    /// <summary>认领账号的标识。</summary>
    public int AccountId { get; set; }

    /// <summary>认领时刻（审计用）。</summary>
    public DateTimeOffset BoundAt { get; set; }
}
