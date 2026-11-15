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

internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<PlatformUserEntity> Users => Set<PlatformUserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformUserEntity>(entity =>
        {
            entity.ToTable("platform_users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).ValueGeneratedNever();
            entity.Property(u => u.FirstName).IsRequired();
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
