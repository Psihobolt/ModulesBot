using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modules.MovieNight.Application;

namespace Modules.MovieNight.Tests.Helpers;

/// <summary>
/// Реальный Program (с модулем MovieNight): токен тестового бота, временный SQLite, FakeClock,
/// подменены IMessageSender (RecordingSender) и IInviteCodeGenerator (ScriptedCodes). IDeepLinks — реальный.
/// </summary>
public sealed class MovieNightFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = TempDir.Create();

    /// <summary>Bot:Username; null = не задан (ссылки null).</summary>
    public string? Username { get; init; } = "MovieNightBot";

    /// <summary>Bot:MiniAppShortName; null = не задан.</summary>
    public string? MiniAppShortName { get; init; } = "app";

    /// <summary>Bot:PublicUrl; null = не задан (нет WebAppUrl, BotSetupService ничего не делает).</summary>
    public string? PublicUrl { get; init; }

    public FakeClock Clock { get; } = new();

    /// <summary>Подмена primary handler именованного HttpClient "telegram": запись и фейковые ответы Bot API.</summary>
    public FakeTelegramHandler BotApi { get; } = new();

    public LogCapture Logs { get; } = new();

    public RecordingSender Sender { get; } = new();

    public ScriptedCodes Codes { get; } = new();

    public string ConnectionString => $"Data Source={Path.Combine(_dir, "test.db")};Pooling=False";

    public long Scalar(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public string? ScalarString(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Bot:Token", TestInitData.BotToken);
        builder.UseSetting("Bot:WebhookSecret", "test-webhook-secret");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("WebApp:DistPath", Path.Combine(_dir, "no-dist"));
        if (Username is not null) builder.UseSetting("Bot:Username", Username);
        if (MiniAppShortName is not null) builder.UseSetting("Bot:MiniAppShortName", MiniAppShortName);
        if (PublicUrl is not null) builder.UseSetting("Bot:PublicUrl", PublicUrl);
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
            services.RemoveAll<IMessageSender>();
            services.AddSingleton<IMessageSender>(Sender);
            services.RemoveAll<IInviteCodeGenerator>();
            services.AddSingleton<IInviteCodeGenerator>(Codes);
            // Поздняя регистрация поверх платформенной подменяет primary handler (проверено тестом AC1 setMyCommands).
            services.AddHttpClient("telegram").ConfigurePrimaryHttpMessageHandler(() => BotApi);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TempDir.TryDelete(_dir);
    }

    /// <summary>POST /bot/webhook с секретом; после ответа все эффекты обновления уже случились.</summary>
    public async Task<HttpResponseMessage> PostUpdateAsync(global::Telegram.Bot.Types.Update update)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bot/webhook");
        request.Headers.TryAddWithoutValidation("X-Telegram-Bot-Api-Secret-Token", "test-webhook-secret");
        request.Content = new StringContent(TestUpdates.ToJson(update), Encoding.UTF8, "application/json");
        return await CreateClient().SendAsync(request);
    }

    // ---------------- HTTP helpers ----------------

    /// <summary>Запрос от имени пользователя (null = без Authorization). json = сырое тело (Content-Type application/json).</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, long? userId, string? json = null,
        string? authorization = null, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, path);
        var auth = authorization ?? (userId is { } id ? ApiCall.Tma(TestInitData.For(id)) : null);
        if (auth is not null) request.Headers.TryAddWithoutValidation("Authorization", auth);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, contentType);
        return await CreateClient().SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(long? user, string path) => SendAsync(HttpMethod.Get, path, user);

    public Task<HttpResponseMessage> PostAsync(long? user, string path, string? json = null) =>
        SendAsync(HttpMethod.Post, path, user, json);

    public Task<HttpResponseMessage> PutAsync(long? user, string path, string json) =>
        SendAsync(HttpMethod.Put, path, user, json);

    public Task<HttpResponseMessage> DeleteAsync(long? user, string path) => SendAsync(HttpMethod.Delete, path, user);

    public static string NameBody(string? name) => JsonSerializer.Serialize(new { name });

    /// <summary>Регистрирует пользователя в реестре платформы через GET /api/platform/me.</summary>
    public async Task SeedAsync(long id, string firstName, string? lastName = null, string? username = null,
        bool? allowsWriteToPm = true)
    {
        var raw = TestInitData.For(id, firstName: firstName, lastName: lastName, username: username, allowsWriteToPm: allowsWriteToPm);
        var response = await SendAsync(HttpMethod.Get, "/api/platform/me", null, authorization: ApiCall.Tma(raw));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Создаёт пользователей 1..n: Аня, Петя, Вася, Гоша, Дима, ... (все с CanReceiveMessages).</summary>
    public async Task SeedDefaultsAsync(int count = 4)
    {
        string[] names = ["Аня", "Петя", "Вася", "Гоша", "Дима", "Егор", "Женя"];
        for (var i = 1; i <= count; i++) await SeedAsync(i, names[(i - 1) % names.Length]);
    }

    public async Task<JsonElement> CreateGroupAsync(long owner, string name = "Группа")
    {
        var response = await PostAsync(owner, "/api/movienight/groups", NameBody(name));
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await ApiCall.JsonAsync(response);
    }

    public async Task<JsonElement> JoinAsync(long user, string code)
    {
        var response = await PostAsync(user, $"/api/movienight/invites/{code}/join");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await ApiCall.JsonAsync(response);
    }

    public async Task<JsonElement> GetGroupAsync(long user, string id)
    {
        var response = await GetAsync(user, $"/api/movienight/groups/{id}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await ApiCall.JsonAsync(response);
    }

    /// <summary>Группа: владелец + остальные вступают по порядку. Возвращает (id, code).</summary>
    public async Task<(string Id, string Code)> GroupWithAsync(string name, long owner, params long[] members)
    {
        var g = await CreateGroupAsync(owner, name);
        var id = g.GetProperty("id").GetString()!;
        var code = g.GetProperty("invite").GetProperty("code").GetString()!;
        foreach (var m in members) await JoinAsync(m, code);
        return (id, code);
    }

    public static long[] MemberIds(JsonElement group) =>
        group.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("userId").GetInt64()).ToArray();

    public static async Task AssertGroupProblemAsync(HttpResponseMessage response, int status, string code, string title)
    {
        var body = await ApiCall.AssertProblemAsync(response, status, code);
        Assert.Equal(title, body.GetProperty("title").GetString());
    }
}
