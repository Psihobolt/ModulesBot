using Bot.Platform.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bot.Platform.Tests.Helpers;

/// <summary>
/// The real <c>Program</c> (no modules) with test configuration: temp SQLite file, fake clock, captured logs.
/// Set <see cref="Token"/> = null to start without Bot:Token.
/// </summary>
public sealed class PlatformFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = TempDir.Create();

    public string? Token { get; init; } = TestInitData.BotToken;

    /// <summary>Bot:WebhookSecret (002); null = not set at all.</summary>
    public string? WebhookSecret { get; init; } = TestBot.WebhookSecret;

    /// <summary>Hook to override services (IBotApi, IUserRegistry, ...) after the application ones.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Absolute path for WebApp:DistPath; default is a non-existent folder.</summary>
    public string? DistPath { get; init; }

    public Dictionary<string, string?> Settings { get; } = [];

    public FakeClock Clock { get; } = new();

    public LogCapture Logs { get; } = new();

    public string ConnectionString => $"Data Source={Path.Combine(_dir, "test.db")};Pooling=False";

    public long Scalar(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" environment: user-secrets / Development settings of the developer machine are not loaded.
        builder.UseEnvironment("Testing");
        if (Token is not null)
        {
            builder.UseSetting("Bot:Token", Token);
        }

        if (WebhookSecret is not null)
        {
            builder.UseSetting("Bot:WebhookSecret", WebhookSecret);
        }

        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("WebApp:DistPath", DistPath ?? Path.Combine(_dir, "no-dist"));
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            ConfigureServices?.Invoke(services);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TempDir.TryDelete(_dir);
    }
}
