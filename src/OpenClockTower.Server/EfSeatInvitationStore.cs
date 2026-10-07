using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现的席位邀请凭据表（D-0038）。</summary>
public sealed class EfSeatInvitationStore : ISeatInvitationStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造凭据表。</summary>
    public EfSeatInvitationStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeatInvitation>> ListByGameAsync(
        GameId gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SeatInvitations
            .AsNoTracking()
            .Where(item => item.GameId == gameId.Value)
            .OrderBy(item => item.Seat)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToInvitation)];
    }

    /// <inheritdoc />
    public async Task ReplaceAsync(GameId gameId, SeatInvitation invitation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.SeatInvitations.FirstOrDefaultAsync(
            item => item.GameId == gameId.Value && item.Seat == invitation.Seat.Value,
            cancellationToken);

        if (row is null)
        {
            db.SeatInvitations.Add(new SeatInvitationEntity
            {
                GameId = gameId.Value,
                Seat = invitation.Seat.Value,
                Hash = invitation.Hash,
                ExpiresAt = invitation.ExpiresAt,
            });
        }
        else
        {
            // 覆盖 = 轮换：旧哈希就此消失，旧的那一枚邀请码再也没法被核验通过。
            row.Hash = invitation.Hash;
            row.ExpiresAt = invitation.ExpiresAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static SeatInvitation ToInvitation(SeatInvitationEntity row) => new()
    {
        Seat = new SeatId(row.Seat),
        Hash = row.Hash,
        ExpiresAt = row.ExpiresAt,
    };
}
