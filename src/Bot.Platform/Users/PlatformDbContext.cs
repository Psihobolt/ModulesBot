using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bot.Platform.Users;

internal sealed class PlatformUserEntity
{
    public long Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string? LastName { get; set; }

    public string? Username { get; set; }

    public string? LanguageCode { get; set; }

    public bool CanReceiveMessages { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}

internal sealed class PendingReplyEntity
{
    public long UserId { get; set; }

    public string Module { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<PlatformUserEntity> Users => Set<PlatformUserEntity>();

    public DbSet<PendingReplyEntity> PendingReplies => Set<PendingReplyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformUserEntity>(entity =>
        {
            entity.ToTable("platform_users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).ValueGeneratedNever();
            entity.Property(u => u.FirstName).IsRequired();
        });

        modelBuilder.Entity<PendingReplyEntity>(entity =>
        {
            entity.ToTable("platform_pending_replies");
            entity.HasKey(r => r.UserId);
            entity.Property(r => r.UserId).ValueGeneratedNever();
            entity.Property(r => r.Module).IsRequired();
            entity.Property(r => r.Action).IsRequired();
            entity.Property(r => r.Payload).IsRequired();
        });
    }
}

// Только для dotnet ef (design-time).
internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlite("Data Source=design-time.db", sqlite => sqlite.MigrationsHistoryTable("__ef_migrations_platform"))
            .Options;
        return new PlatformDbContext(options);
    }
}
