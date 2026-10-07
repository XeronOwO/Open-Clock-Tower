using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// EF Core + SQLite 实现的账号注销端口（M5 / G-A1-6）：账号行 + 席位绑定 + 桌归属，一个事务。
/// </summary>
/// <remarks>
/// <para>
/// 三件事必须同批成立，否则会留下自相矛盾的状态：账号没了但席位还占着（后来者坐不进去）、
/// 归属指向一个已经不存在的账号（那一桌永远没有主持台，却看不出来为什么）。
/// </para>
/// <para>
/// 受影响桌的标识要在删除**之前**读出来：删完再问"刚才删了哪些桌"就没有答案了，
/// 而调用方要靠它去刷新那些桌内存里的席位名读模型（注销必须当场生效，不能等重启）。
/// </para>
/// </remarks>
public sealed class EfAccountErasureStore : IAccountErasureStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造存储。</summary>
    public EfAccountErasureStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<AccountErasureResult> EraseAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var seatTables = await db.SeatBindings
            .AsNoTracking()
            .Where(row => row.AccountId == accountId.Value)
            .Select(row => row.GameId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var ownedTables = await db.Games
            .AsNoTracking()
            .Where(row => row.CreatedByAccountId == accountId.Value)
            .Select(row => row.GameId)
            .ToListAsync(cancellationToken);

        var bindings = await db.SeatBindings
            .Where(row => row.AccountId == accountId.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // 归属置空而不是连带删桌：那一桌上可能正有别人的对局（见 IAccountErasureStore 的说明）。
        var released = await db.Games
            .Where(row => row.CreatedByAccountId == accountId.Value)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.CreatedByAccountId, (int?)null),
                cancellationToken);

        var deleted = await db.Users
            .Where(row => row.Id == accountId.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new AccountErasureResult(
            deleted > 0,
            bindings,
            released,
            [.. seatTables.Concat(ownedTables).Distinct(StringComparer.Ordinal).Select(id => new GameId(id))]);
    }
}
