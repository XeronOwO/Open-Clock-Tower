namespace OpenClockTower.Server;

/// <summary>宿主配置：演示局 + 节奏配额 + 数据库位置。</summary>
public sealed class GameServerOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer";

    /// <summary>默认游戏标识（首版单局单进程）。</summary>
    public string GameId { get; set; } = "default";

    /// <summary>单局席位数（建局能力落地前的配置输入）。</summary>
    public int SeatCount { get; set; } = 5;

    /// <summary>SQLite 数据库路径（相对内容根）。</summary>
    public string DatabasePath { get; set; } = "openclocktower.db";

    /// <summary>每个槽位的最短配额（秒），默认 10（D-0013）。</summary>
    public double SlotQuotaSeconds { get; set; } = 10;

    /// <summary>节拍器心跳间隔（毫秒）。</summary>
    public int PacerIntervalMilliseconds { get; set; } = 200;
}
