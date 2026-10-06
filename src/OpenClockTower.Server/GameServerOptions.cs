namespace OpenClockTower.Server;

/// <summary>宿主配置：默认桌 + 节奏配额 + 数据库位置 + 管理员名单。</summary>
public sealed class GameServerOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer";

    /// <summary>
    /// 默认桌标识：连接未声明 <c>?gameId=</c> 时回落到它，启动引导也保证它存在
    /// （升级前的那一桌就是它，多桌 D-0024 之后依然如此）。
    /// </summary>
    public string GameId { get; set; } = "default";

    /// <summary>默认桌的席位数。</summary>
    public int SeatCount { get; set; } = 5;

    /// <summary>SQLite 数据库路径（相对内容根）。</summary>
    public string DatabasePath { get; set; } = "openclocktower.db";

    /// <summary>每个槽位的最短配额（秒），默认 10（D-0013）。</summary>
    public double SlotQuotaSeconds { get; set; } = 10;

    /// <summary>节拍器心跳间隔（毫秒）。</summary>
    public int PacerIntervalMilliseconds { get; set; } = 200;

    /// <summary>
    /// 管理员的**登录名**清单（D-0025：只有管理员能开桌）。
    /// </summary>
    /// <remarks>
    /// 刻意放在配置而不进库：它是部署者的授权名单，不是玩家数据；也避免为它改库结构。
    /// 冷启动靠部署时指定第一个管理员。**留空 = 任何人都不能开桌**（宁可不给，也不默认放开）。
    /// </remarks>
    public string[] AdminUsernames { get; set; } = [];
}
