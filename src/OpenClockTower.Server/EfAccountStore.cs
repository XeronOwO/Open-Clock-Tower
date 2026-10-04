using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现的账号表（D-0021）。</summary>
public sealed class EfAccountStore : IAccountStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造账号表。</summary>
    public EfAccountStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<Account?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var key = UsernameText.ComparisonKeyOf(username);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UsernameKey == key, cancellationToken);
        return row is null ? null : ToAccount(row);
    }

    /// <inheritdoc />
    public async Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id.Value, cancellationToken);
        return row is null ? null : ToAccount(row);
    }

    /// <inheritdoc />
    public async Task<Account?> TryCreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        var key = UsernameText.ComparisonKeyOf(account.Username);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = new UserEntity
        {
            UsernameKey = key,
            Username = account.Username,
            DisplayName = account.DisplayName,
            PasswordHash = account.PasswordHash,
            RecoveryCodeHash = account.RecoveryCodeHash,
        };
        db.Users.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ToAccount(entity);
        }
        catch (DbUpdateException)
        {
            // 唯一索引挡下的竞态：再确认一次确实是"登录名已占用"，是则按业务拒绝，否则原样抛。
            await using var verify = await _factory.CreateDbContextAsync(cancellationToken);
            if (await verify.Users.AsNoTracking().AnyAsync(item => item.UsernameKey == key, cancellationToken))
            {
                return null;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(Account account, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(item => item.Id == account.Id.Value, cancellationToken);
        if (row is null)
        {
            return false;
        }

        row.UsernameKey = UsernameText.ComparisonKeyOf(account.Username);
        row.Username = account.Username;
        row.DisplayName = account.DisplayName;
        row.PasswordHash = account.PasswordHash;
        row.RecoveryCodeHash = account.RecoveryCodeHash;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Account ToAccount(UserEntity row) => new()
    {
        Id = new AccountId(row.Id),
        Username = row.Username,
        DisplayName = row.DisplayName,
        PasswordHash = row.PasswordHash,
        RecoveryCodeHash = row.RecoveryCodeHash,
    };
}
