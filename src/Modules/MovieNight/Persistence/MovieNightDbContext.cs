using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.MovieNight.Domain;

namespace Modules.MovieNight.Persistence;

internal sealed class MovieNightDbContext(DbContextOptions<MovieNightDbContext> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> Members => Set<GroupMember>();

    public DbSet<RevokedInviteCode> RevokedInviteCodes => Set<RevokedInviteCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Group>(entity =>
        {
            entity.ToTable("movienight_groups");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Id).ValueGeneratedNever();
            entity.Property(g => g.Name).IsRequired();
            entity.Property(g => g.InviteCode).IsRequired();
            entity.HasIndex(g => g.InviteCode).IsUnique();
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.ToTable("movienight_group_members");
            entity.HasKey(m => m.JoinSeq);
            // long-ключ + ValueGeneratedOnAdd => INTEGER PRIMARY KEY AUTOINCREMENT (значения не переиспользуются).
            entity.Property(m => m.JoinSeq).ValueGeneratedOnAdd();
            entity.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
            entity.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<RevokedInviteCode>(entity =>
        {
            entity.ToTable("movienight_revoked_invite_codes");
            entity.HasKey(r => r.Code);
            entity.Property(r => r.Code).ValueGeneratedNever();
        });
    }
}

// Только для dotnet ef (design-time).
internal sealed class MovieNightDbContextFactory : IDesignTimeDbContextFactory<MovieNightDbContext>
{
    public MovieNightDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MovieNightDbContext>()
            .UseSqlite("Data Source=design-time.db", sqlite => sqlite.MigrationsHistoryTable("__ef_migrations_movienight"))
            .Options;
        return new MovieNightDbContext(options);
    }
}
