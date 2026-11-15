using System.Net.Http.Headers;
using System.Text;
using Bot.Platform.Auth;
using Bot.Platform.Messaging;
using Bot.Platform.Users;
using NSubstitute;
using Telegram.Bot.Types;

namespace Bot.Platform.Tests.Helpers;

/// <summary>Shortcuts shared by the bot tests (TEST-2..TEST-7).</summary>
internal static class BotHarness
{
    /// <summary>Host with demo module + mocked IBotApi; optional substitute IUserRegistry (scoped) and extras.</summary>
    public static Task<DemoHost> StartAsync(
        IUserRegistry? registry = null,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplicationBuilder>? addModules = null,
        bool includeDemoModule = true,
        bool mockBotApi = true,
        string? token = TestInitData.BotToken,
        string? webhookSecret = TestBot.WebhookSecret) =>
        DemoHost.StartAsync(
            settings,
            token,
            webhookSecret,
            services =>
            {
                if (registry is not null)
                {
                    services.AddScoped(_ => registry);
                }

                configureServices?.Invoke(services);
            },
            addModules,
            mockBotApi,
            includeDemoModule);

    public static IUserRegistry NewRegistry()
    {
        var registry = Substitute.For<IUserRegistry>();
        registry.UpsertAsync(default!, default, default).ReturnsForAnyArgs(Task.CompletedTask);
        registry.MarkCannotReceiveMessagesAsync(default, default).ReturnsForAnyArgs(Task.CompletedTask);
        return registry;
    }

    public static Task DispatchAsync(this DemoHost host, Update update, CancellationToken ct = default) =>
        host.Services.GetRequiredService<IUpdateDispatcher>().DispatchAsync(update, ct);

    public static Dictionary<string, string?> WithPublicUrl(string url = TestBot.PublicUrl) =>
        new() { ["Bot:PublicUrl"] = url };

    public static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client, string? body, string? secret = TestBot.WebhookSecret, string path = "/bot/webhook")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body ?? "", Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        if (secret is not null)
        {
            request.Headers.TryAddWithoutValidation(BotDefaults.SecretHeader, secret);
        }

        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, Update update, string? secret = TestBot.WebhookSecret) =>
        PostWebhookAsync(client, TestUpdates.ToJson(update), secret);

    // ---- 003: pending replies (platform_pending_replies) ----

    public sealed record PendingRow(long UserId, string Module, string Action, string Payload);

    public static async Task ExpectAsync(this DemoHost host, long userId, string action = "name", string? payload = "p1",
        string module = "demo")
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBotConversation>()
            .ExpectReplyAsync(userId, module, action, payload, CancellationToken.None);
    }

    public static async Task<bool> CancelPendingAsync(this DemoHost host, long userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBotConversation>().CancelAsync(userId, CancellationToken.None);
    }

    private static Microsoft.Data.Sqlite.SqliteConnection Open(DemoHost host)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(host.App.Configuration["ConnectionStrings:Default"]);
        connection.Open();
        return connection;
    }

    public static List<PendingRow> PendingRows(this DemoHost host, long? userId = null)
    {
        using var connection = Open(host);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId, Module, Action, Payload FROM platform_pending_replies" +
                              (userId is null ? "" : " WHERE UserId = " + userId);
        using var reader = command.ExecuteReader();
        var rows = new List<PendingRow>();
        while (reader.Read())
        {
            rows.Add(new PendingRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    /// <summary>Simulates a module that renamed its action after deploy.</summary>
    public static void RenamePendingAction(this DemoHost host, long userId, string newAction)
    {
        using var connection = Open(host);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE platform_pending_replies SET Action = $a WHERE UserId = $u";
        command.Parameters.AddWithValue("$a", newAction);
        command.Parameters.AddWithValue("$u", userId);
        command.ExecuteNonQuery();
    }
}
