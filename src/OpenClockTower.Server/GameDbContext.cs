using Microsoft.EntityFrameworkCore;

namespace OpenClockTower.Server;

/// <summary>持久化上下文：事件 / 快照 / 回执 / 会话信息 / 账号 / 席位绑定 / 席位邀请。</summary>
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

    /// <summary>会话信息（桌的席位名单与大厅元数据）。</summary>
    public DbSet<GameSetupEntity> Games => Set<GameSetupEntity>();

    /// <summary>账号（D-0021：全局身份，跨局持久）。</summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>席位绑定（D-0021：会话层的「席位 ↔ 账号」认领关系）。</summary>
    public DbSet<SeatBindingEntity> SeatBindings => Set<SeatBindingEntity>();

    /// <summary>席位邀请凭据（D-0038：一个席位一行，只存邀请码的哈希与到期时刻）。</summary>
    public DbSet<SeatInvitationEntity> SeatInvitations => Set<SeatInvitationEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventEntity>().HasKey(entity => new { entity.GameId, entity.Sequence });
        modelBuilder.Entity<SnapshotEntity>().HasKey(entity => entity.GameId);
        modelBuilder.Entity<ReceiptEntity>().HasKey(entity => new { entity.GameId, entity.IdempotencyKey });
        modelBuilder.Entity<GameSetupEntity>().HasKey(entity => entity.GameId);
        modelBuilder.Entity<UserEntity>().HasKey(entity => entity.Id);
        modelBuilder.Entity<UserEntity>().HasIndex(entity => entity.UsernameKey).IsUnique();
        modelBuilder.Entity<SeatBindingEntity>().HasKey(entity => new { entity.GameId, entity.Seat });
        modelBuilder.Entity<SeatBindingEntity>()
            .HasIndex(entity => new { entity.GameId, entity.AccountId })
            .IsUnique();
        modelBuilder.Entity<SeatInvitationEntity>().HasKey(entity => new { entity.GameId, entity.Seat });
    }
}
