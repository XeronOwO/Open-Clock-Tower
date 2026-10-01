namespace OpenClockTower.Application;

/// <summary>一局的会话信息：谁拿着哪张票据（服务端持久化，重启后仍有效）。</summary>
public sealed record GameSetup
{
    /// <summary>游戏标识。</summary>
    public required GameId GameId { get; init; }

    /// <summary>各席位的票据。</summary>
    public required IReadOnlyList<SeatTicket> Seats { get; init; }

    /// <summary>说书人票据。</summary>
    public required string StorytellerTicket { get; init; }
}
