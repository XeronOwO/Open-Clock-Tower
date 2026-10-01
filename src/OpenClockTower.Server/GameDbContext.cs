using Microsoft.EntityFrameworkCore;

namespace OpenClockTower.Server;

/// <summary>持久化上下文：事件 / 快照 / 回执 / 会话票据。</summary>
public sealed class GameDbContext : DbContext
{
    /// <summary>构造上下文。</summary>
    public GameDbContext(DbContextOptions<GameDbContext> options)
        : base(options)
    {
    }

    /// <summary>事件流（唯一事实来源）。</summary>
    public DbSet<EventEntity> Events => Set<EventEntity>();

    /// <summary>状态快照。</summary>
    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();

    /// <summary>幂等回执。</summary>
    public DbSet<ReceiptEntity> Receipts => Set<ReceiptEntity>();

    /// <summary>会话票据。</summary>
    public DbSet<GameSetupEntity> Games => Set<GameSetupEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventEntity>().HasKey(entity => new { entity.GameId, entity.Sequence });
        modelBuilder.Entity<SnapshotEntity>().HasKey(entity => entity.GameId);
        modelBuilder.Entity<ReceiptEntity>().HasKey(entity => new { entity.GameId, entity.IdempotencyKey });
        modelBuilder.Entity<GameSetupEntity>().HasKey(entity => entity.GameId);
    }
}
