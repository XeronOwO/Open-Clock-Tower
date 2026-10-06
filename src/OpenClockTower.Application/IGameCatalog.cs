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
}
