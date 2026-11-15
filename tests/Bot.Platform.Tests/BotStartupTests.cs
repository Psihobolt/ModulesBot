using System.Net;
using System.Text.Json;
using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Bot.Platform.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot.Platform.Tests;

// TEST-6: AC-19..AC-21, AC-23
public class BotStartupTests
{
    private static readonly string[] ExpectedCommands =
    [
        "start|открыть приложение", "help|список команд", "ping|Проверка связи", "list|Список", "zeta|Последняя",
    ];

    private static Task<DemoHost> StartWith(IBotApi api, Dictionary<string, string?>? settings) =>
        BotHarness.StartAsync(settings: settings, mockBotApi: false,
            configureServices: s => s.AddSingleton(api),
            addModules: b => b.AddModule<DemoBModule>());

    private static IBotApi NewApi() => BotApiMock.Create();

    private static IEnumerable<(string Url, string Secret, UpdateType[] Allowed)> WebhookCalls(IBotApi api) =>
        api.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IBotApi.SetWebhookAsync))
            .Select(c => c.GetArguments())
            .Select(a => ((string)a[0]!, (string)a[1]!, ((IReadOnlyCollection<UpdateType>)a[2]!).ToArray()));

    private static IEnumerable<string[]> CommandCalls(IBotApi api) =>
        api.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IBotApi.SetMyCommandsAsync))
            .Select(c => ((IReadOnlyList<BotCommand>)c.GetArguments()[0]!).Select(x => x.Command + "|" + x.Description).ToArray());

    // ---------------- AC-19 ----------------

    [Theory]
    [InlineData("https://example.org")]
    [InlineData("https://example.org/")]
    public async Task AC19_PublicUrl_RegistersWebhookAndCommandsOnStart(string publicUrl)
    {
        var api = NewApi();
        await using var host = await StartWith(api, BotHarness.WithPublicUrl(publicUrl));

        var webhook = Assert.Single(WebhookCalls(api));
        Assert.Equal("https://example.org/bot/webhook", webhook.Url);
        Assert.Equal(TestBot.WebhookSecret, webhook.Secret);
        Assert.Equal([UpdateType.Message, UpdateType.CallbackQuery], webhook.Allowed);
        Assert.Equal(ExpectedCommands, Assert.Single(CommandCalls(api)));
    }

    [Fact]
    public async Task AC19_FirstCallThrows_ErrorLoggedSecondStillCalledHostKeepsWorking()
    {
        var api = NewApi();
        api.SetWebhookAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("webhook-down"));
        await using var host = await StartWith(api, BotHarness.WithPublicUrl());

        Assert.Single(CommandCalls(api));
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task AC19_FirstCallThrowsSynchronously_SecondStillCalled()
    {
        var api = NewApi();
        api.SetWebhookAsync(default!, default!, default!, default).ReturnsForAnyArgs<Task>(_ => throw new InvalidOperationException("sync-boom"));
        await using var host = await StartWith(api, BotHarness.WithPublicUrl());

        Assert.Single(CommandCalls(api));
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AC19_SecondCallThrows_ErrorLoggedHostKeepsWorking()
    {
        var api = NewApi();
        api.SetMyCommandsAsync(default!, default).ThrowsAsyncForAnyArgs(TestBot.TooManyRequests(1));
        await using var host = await StartWith(api, BotHarness.WithPublicUrl());

        Assert.Single(WebhookCalls(api));
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.Category.Contains("Bot"));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task AC19_PublicUrlNotSet_NoCalls()
    {
        var api = NewApi();
        await using var host = await StartWith(api, null);

        Assert.Equal(0, api.CallCount());
    }

    [Fact]
    public async Task AC19_SetupOnStartupFalse_NoCalls()
    {
        var api = NewApi();
        var settings = BotHarness.WithPublicUrl();
        settings["Bot:SetupOnStartup"] = "false";
        await using var host = await StartWith(api, settings);

        Assert.Equal(0, api.CallCount());
    }

    // ---------------- AC-20 ----------------

    private static async Task<Exception> StartFailureAsync(
        string? secret = TestBot.WebhookSecret, string? publicUrl = null, string? maxRetryAfter = null)
    {
        var settings = new Dictionary<string, string?>();
        if (publicUrl is not null) settings["Bot:PublicUrl"] = publicUrl;
        if (maxRetryAfter is not null) settings["Bot:MaxRetryAfter"] = maxRetryAfter;
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var host = await BotHarness.StartAsync(settings: settings, webhookSecret: secret);
        });
        Assert.NotNull(ex);
        return ex;
    }

    private static void AssertNamesKeyNotValue(Exception ex, string key, string? value)
    {
        Assert.IsType<OptionsValidationException>(ex);
        Assert.Contains(key, ex.Message);
        if (!string.IsNullOrEmpty(value)) Assert.DoesNotContain(value, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad secret zz")]
    [InlineData("bad.secret.zz")]
    [InlineData("секрет")]
    public async Task AC20_InvalidWebhookSecret_StartupFailsNamingKeyNotValue(string? secret)
    {
        var ex = await StartFailureAsync(secret: secret);

        AssertNamesKeyNotValue(ex, "Bot:WebhookSecret", secret?.Trim());
    }

    [Fact]
    public async Task AC20_WebhookSecretLongerThan256_Fails()
    {
        var secret = new string('q', 257);

        var ex = await StartFailureAsync(secret: secret);

        AssertNamesKeyNotValue(ex, "Bot:WebhookSecret", secret);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("aZ09_-")]
    public async Task AC20_ValidWebhookSecret_Starts(string secret)
    {
        await using var host = await BotHarness.StartAsync(webhookSecret: secret);
    }

    [Fact]
    public async Task AC20_WebhookSecretOf256Chars_Starts()
    {
        await using var host = await BotHarness.StartAsync(webhookSecret: new string('a', 256));
    }

    [Theory]
    [InlineData("http://zzhost.example")]
    [InlineData("/zz-relative")]
    [InlineData("zzhost.example")]
    [InlineData("ftp://zzhost.example")]
    [InlineData("https://zzhost.example/?zzq=1")]
    [InlineData("https://zzhost.example/#zzf")]
    public async Task AC20_InvalidPublicUrl_StartupFailsNamingKeyNotValue(string url)
    {
        var ex = await StartFailureAsync(publicUrl: url);

        AssertNamesKeyNotValue(ex, "Bot:PublicUrl", "zz");
    }

    [Theory]
    [InlineData("-00:00:01")]
    [InlineData("00:01:01")]
    [InlineData("1.00:00:00")]
    public async Task AC20_MaxRetryAfterOutOfRange_StartupFailsNamingKey(string value)
    {
        var ex = await StartFailureAsync(maxRetryAfter: value);

        AssertNamesKeyNotValue(ex, "Bot:MaxRetryAfter", value);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:01:00")]
    public async Task AC20_MaxRetryAfterAtBoundary_Starts(string value)
    {
        await using var host = await BotHarness.StartAsync(settings: new() { ["Bot:MaxRetryAfter"] = value });
    }

    // ---------------- AC-21 ----------------

    [Fact]
    public async Task AC21_RealClientOverStub_NoTokenOrSecretInAnyLog()
    {
        const string token = "987654:AAH-Distinct_TokenValue123";
        const string secret = "distinct-secret-value-xyz";
        var handler = new StubHttpMessageHandler();
        var settings = BotHarness.WithPublicUrl();
        await using var host = await BotHarness.StartAsync(
            settings: settings, token: token, webhookSecret: secret, mockBotApi: false,
            configureServices: s => s.AddHttpClient(BotDefaults.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

        // normal flow, handler exception, 403, 429, 400
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start(), secret);
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Command("ping", "boom"), secret);
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Callback("demo:act:1"), secret);
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start(), "wrong-" + secret);
        handler.Default = _ => StubHttpMessageHandler.Json(HttpStatusCode.Forbidden, StubHttpMessageHandler.ApiError(403, "Forbidden: bot was blocked by the user"));
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start(), secret);
        handler.Default = _ => StubHttpMessageHandler.Json((HttpStatusCode)429, StubHttpMessageHandler.ApiError(429, "Too Many Requests", "{\"retry_after\":3}"));
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start(), secret);
        handler.Default = _ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, StubHttpMessageHandler.ApiError(400, "Bad Request: query is too old"));
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Callback("demo:act:1"), secret);

        Assert.Contains(handler.Requests, r => r.Uri.ToString().Contains(token));   // the real client path was exercised
        Assert.True(host.Logs.Entries.Count > 10);
        Assert.DoesNotContain(host.Logs.Entries, e => e.FullText.Contains(token) || e.FullText.Contains("AAH-Distinct"));
        Assert.DoesNotContain(host.Logs.Entries, e => e.FullText.Contains(secret));
        Assert.DoesNotContain(host.Logs.Entries, e => e.Category.StartsWith("System.Net.Http") && e.FullText.Contains("sendMessage"));
    }

    // ---------------- AC-23 ----------------

    private static async Task<(DemoHost Host, IBotApi Api, StubHttpMessageHandler Handler)> StartRealAsync()
    {
        var handler = new StubHttpMessageHandler();
        var host = await BotHarness.StartAsync(
            mockBotApi: false,
            configureServices: s => s.AddHttpClient(BotDefaults.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        return (host, host.Services.GetRequiredService<IBotApi>(), handler);
    }

    [Fact]
    public async Task AC23_OkResponse_ReturnsMessageIdAndPostsSendMessagePlainText()
    {
        var (host, api, handler) = await StartRealAsync();
        await using var _ = host;
        handler.Enqueue(HttpStatusCode.OK, StubHttpMessageHandler.OkMessage(7));

        var id = await api.SendMessageAsync(4242, "hello", null, false, CancellationToken.None);

        Assert.Equal(7, id);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/sendMessage", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(4242, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("hello", body.RootElement.GetProperty("text").GetString());
        Assert.False(body.RootElement.TryGetProperty("parse_mode", out var pm) && pm.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task AC23_HtmlTrue_SendsParseModeHtml()
    {
        var (host, api, handler) = await StartRealAsync();
        await using var _ = host;

        await api.SendMessageAsync(1, "<b>x</b>", null, true, CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("HTML", body.RootElement.GetProperty("parse_mode").GetString(), ignoreCase: true);   // Telegram.Bot 22 writes "Html"; Bot API is case-insensitive
    }

    [Fact]
    public async Task AC23_ReplyMarkup_IsSerializedIntoRequest()
    {
        var (host, api, handler) = await StartRealAsync();
        await using var _ = host;
        var markup = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
            Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("Go", "demo:act"));

        await api.SendMessageAsync(1, "x", markup, false, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("demo:act", request.Body);
        Assert.Contains("reply_markup", request.Body);
    }

    [Fact]
    public async Task AC23_Error403_ThrowsApiRequestExceptionWithCode()
    {
        var (host, api, handler) = await StartRealAsync();
        await using var _ = host;
        handler.Enqueue(HttpStatusCode.Forbidden, StubHttpMessageHandler.ApiError(403, "Forbidden: bot was blocked by the user"));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(() => api.SendMessageAsync(1, "x", null, false, CancellationToken.None));

        Assert.Equal(403, ex.ErrorCode);
    }

    [Fact]
    public async Task AC23_Error429_ThrowsWithRetryAfterAndNoBuiltInRetry()
    {
        var (host, api, handler) = await StartRealAsync();
        await using var _ = host;
        handler.Default = _ => StubHttpMessageHandler.Json((HttpStatusCode)429,
            StubHttpMessageHandler.ApiError(429, "Too Many Requests: retry after 3", "{\"retry_after\":3}"));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(() => api.SendMessageAsync(1, "x", null, false, CancellationToken.None));

        Assert.Equal(429, ex.ErrorCode);
        Assert.Equal(3, ex.Parameters?.RetryAfter);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task AC23_AnswerCallbackQuery_PostsToAnswerCallbackQuery()
    {
        var (host, api, handler) = await StartRealAsync();
        await using var _ = host;
        handler.Enqueue(HttpStatusCode.OK, "{\"ok\":true,\"result\":true}");

        await api.AnswerCallbackQueryAsync("cq-1", "txt", true, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/answerCallbackQuery", request.Uri.AbsolutePath);
        Assert.Contains("cq-1", request.Body);
    }

    [Fact]
    public async Task AC23_NoTelegramBotClientRegisteredInDi()
    {
        var (host, _, _) = await StartRealAsync();
        await using var __ = host;

        Assert.Null(host.Services.GetService<Telegram.Bot.ITelegramBotClient>());
    }
}
