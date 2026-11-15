using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bot.Platform.Persistence;

public static class ModuleDbContextExtensions
{
    private const string DefaultConnectionString = "Data Source=data/bot.db";

    /// <summary>
    /// SQLite из ConnectionStrings:Default, история миграций в "__ef_migrations_{moduleName}",
    /// миграции применяются при старте Host.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration config,
        string moduleName)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        // Строка подключения читается при создании контекста, чтобы учитывать переопределения конфигурации.
        services.AddDbContext<TContext>((sp, options) =>
        {
            var connectionString = config.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = DefaultConnectionString;
            }

            connectionString = ResolveConnectionString(connectionString, sp.GetService<IHostEnvironment>()?.ContentRootPath);
            EnsureDirectory(connectionString);
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsHistoryTable($"__ef_migrations_{moduleName}"));
        });

        services.AddSingleton(new MigrationTarget(typeof(TContext)));
        EnsureMigrationRunner(services);
        return services;
    }

    private static void EnsureMigrationRunner(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DatabaseMigrationService)))
        {
            return;
        }

        // Первым в списке: миграции должны завершиться до старта веб-сервера и остальных сервисов.
        services.Insert(0, ServiceDescriptor.Singleton<IHostedService, DatabaseMigrationService>());
    }

    // Относительный путь к файлу БД считается от ContentRootPath, а не от CWD процесса.
    private static string ResolveConnectionString(string connectionString, string? contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(contentRoot)
            || string.IsNullOrWhiteSpace(dataSource)
            || builder.Mode == SqliteOpenMode.Memory
            || dataSource.Equals(":memory:", StringComparison.Ordinal)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(dataSource))
        {
            return connectionString;
        }

        builder.DataSource = Path.GetFullPath(dataSource, contentRoot);
        return builder.ToString();
    }

    private static void EnsureDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || builder.Mode == SqliteOpenMode.Memory
            || dataSource.Equals(":memory:", StringComparison.Ordinal)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

internal sealed record MigrationTarget(Type ContextType);

internal sealed class DatabaseMigrationService(IServiceProvider services, IEnumerable<MigrationTarget> targets)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var target in targets.DistinctBy(t => t.ContextType))
        {
            await using var scope = services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(target.ContextType);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
