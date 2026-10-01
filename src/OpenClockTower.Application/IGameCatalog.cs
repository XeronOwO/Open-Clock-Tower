namespace OpenClockTower.Application;

/// <summary>会话信息（票据）的持久化端口。</summary>
public interface IGameCatalog
{
    /// <summary>读取一局的会话信息；不存在时为 null。</summary>
    Task<GameSetup?> FindAsync(GameId gameId, CancellationToken cancellationToken);

    /// <summary>保存（新建或覆盖）一局的会话信息。</summary>
    Task SaveAsync(GameSetup setup, CancellationToken cancellationToken);
}
