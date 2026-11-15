using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Bot.Platform.Time;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Bot.Platform.Tests.Helpers;

/// <summary>
/// Host assembled exactly like Program.cs, plus <see cref="DemoModule"/> via <c>AddModule&lt;DemoModule&gt;()</c>
/// (the real Program has no modules). In-memory TestServer, temp SQLite file, fake clock, captured logs.
/// </summary>
public sealed class DemoHost : IAsyncDisposable
{
    private readonly string _dir;

    private DemoHost(WebApplication app, FakeClock clock, LogCapture logs, string dir)
    {
        App = app;
        Clock = clock;
        Logs = logs;
        _dir = dir;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public FakeClock Clock { get; }

    public LogCapture Logs { get; }

    public IServiceProvider Services => App.Services;

    /// <summary>Mocked IBotApi (null when started with <c>mockBotApi: false</c>).</summary>
    internal IBotApi? BotApi { get; private set; }

    /// <param name="settings">Overrides of configuration keys (applied last).</param>
    /// <param name="token">Bot:Token; null = not set.</param>
    /// <param name="webhookSecret">Bot:WebhookSecret; null = not set.</param>
    /// <param name="configureServices">Service overrides, applied after everything else (IUserRegistry, HttpClient handler, ...).</param>
    /// <param name="addModules">Extra modules, added after <see cref="DemoModule"/>.</param>
    /// <param name="mockBotApi">Replace IBotApi by an NSubstitute mock (default); false = real adapter over HttpClient "telegram".</param>
    /// <param name="includeDemoModule">false = host without DemoModule.</param>
    public static async Task<DemoHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        string? token = TestInitData.BotToken,
        string? webhookSecret = TestBot.WebhookSecret,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplicationBuilder>? addModules = null,
        bool mockBotApi = true,
        bool includeDemoModule = true)
    {
        var dir = TempDir.Create();
        var clock = new FakeClock();
        var logs = new LogCapture();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(DemoHost).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();

        var config = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(dir, "test.db")};Pooling=False",
            ["WebApp:DistPath"] = Path.Combine(dir, "no-dist"),
        };
        if (token is not null)
        {
            config["Bot:Token"] = token;
        }

        if (webhookSecret is not null)
        {
            config["Bot:WebhookSecret"] = webhookSecret;
        }

        if (settings is not null)
        {
            foreach (var (k, v) in settings) config[k] = v;
        }

        builder.Configuration.AddInMemoryCollection(config);
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);

        builder.AddPlatform();
        if (includeDemoModule)
        {
            builder.AddModule<DemoModule>();
        }

        addModules?.Invoke(builder);
        builder.Services.AddSingleton<IClock>(clock);
        var botApi = mockBotApi ? BotApiMock.Create() : null;
        if (botApi is not null)
        {
            builder.Services.AddSingleton<IBotApi>(botApi);
        }

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapPlatform();
        await app.StartAsync();
        return new DemoHost(app, clock, logs, dir) { BotApi = botApi };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        TempDir.TryDelete(_dir);
    }
}
