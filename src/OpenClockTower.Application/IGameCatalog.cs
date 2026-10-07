namespace OpenClockTower.Application;

/// <summary>会话信息（票据）的持久化端口。</summary>
public interface IGameCatalog
{
    /// <summary>读取一局的会话信息；不存在时为 null。</summary>
    Task<GameSetup?> FindAsync(GameId gameId, CancellationToken cancellationToken);

    /// <summary>保存（新建或覆盖）一局的会话信息。</summary>
    Task SaveAsync(GameSetup setup, CancellationToken cancellationToken);

    /// <summary>
    /// 列出全部在册的局（按标识升序）。
    /// </summary>
    /// <remarks>
    /// 多桌（D-0024）：宿主启动时据此恢复**每一桌**，大厅列表也据此告诉玩家"现在有哪些桌"。
    /// </remarks>
    Task<IReadOnlyList<GameSetup>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 设置桌名与访问模式（大厅元数据；不影响游戏内状态）。
    /// </summary>
    /// <remarks>
    /// 访问模式只有两种：公开（自助入座）与**邀请制**（要凭邀请码，D-0037）。
    /// 未知的桌**显式失败**，不凭空造一桌——建桌是另一个用例的职责（D-0025）。
    /// </remarks>
    Task UpdateLobbyAsync(
        GameId gameId,
        string name,
        bool isInviteOnly,
        CancellationToken cancellationToken);
}
