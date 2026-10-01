namespace OpenClockTower.Server;

/// <summary>会话表行：席位票据，重启后仍然有效（D-0011 硬约束 2 的前提）。</summary>
public sealed class GameSetupEntity
{
    /// <summary>游戏标识（主键）。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>席位票据 JSON。</summary>
    public string SeatsJson { get; set; } = "[]";

    /// <summary>说书人票据。</summary>
    public string StorytellerTicket { get; set; } = string.Empty;
}
