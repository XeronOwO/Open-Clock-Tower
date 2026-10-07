using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>席位绑定表（D-0021）：只存会话层的认领关系，不写事件流。</summary>
public interface ISeatBindingStore
{
    /// <summary>按席位查本局的绑定；没有返回 null。</summary>
    Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken);

    /// <summary>按账号查本局的绑定；没有返回 null。</summary>
    Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken);

    /// <summary>列出本局全部绑定（启动时装载席位名读模型用）。</summary>
    Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken);

    /// <summary>
    /// 一次列出**多桌**的全部绑定（大厅列表用）。
    /// </summary>
    /// <remarks>
    /// 大厅从前是"每桌查一次"（审计 G-A5-5 的 N+1）：桌数一多，看一眼前厅就要 N 次查询。
    /// 一次 <c>WHERE GameId IN (...)</c> 拿全，调用方按桌分组——查询次数与桌数无关。
    /// </remarks>
    Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
        IReadOnlyCollection<GameId> gameIds,
        CancellationToken cancellationToken);

    /// <summary>写入一条绑定；席位或账号已被占用（含并发竞态）返回 false，不抛异常。</summary>
    Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken);

    /// <summary>解除某席位的绑定（说书人兜底）；原本没有绑定返回 false。</summary>
    Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken);
}
