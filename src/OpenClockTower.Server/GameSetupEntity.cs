namespace OpenClockTower.Server;

/// <summary>会话表行：席位票据与大厅元数据，重启后仍然有效（D-0011 硬约束 2 的前提）。</summary>
/// <remarks>
/// 多桌（D-0024）之后本表每个在册的桌一行，主键仍是游戏标识。
/// <see cref="Name"/> / <see cref="IsLocked"/> 是后加的列：老库靠启动守卫补列
/// （<c>GameBootstrapHostedService.EnsureGameColumnsAsync</c>），不会因为升级而读不出数据。
/// </remarks>
public sealed class GameSetupEntity
{
    /// <summary>游戏标识（主键）。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>席位票据 JSON。</summary>
    public string SeatsJson { get; set; } = "[]";

    /// <summary>说书人票据。</summary>
    public string StorytellerTicket { get; set; } = string.Empty;

    /// <summary>桌名（大厅标题；未命名为空串）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否锁定（不再接受新入座）。</summary>
    public bool IsLocked { get; set; }
}
