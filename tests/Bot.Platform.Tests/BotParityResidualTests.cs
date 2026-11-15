using System.Net;
using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Users;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Bot.Platform.Tests;

// Spec 003, TEST-5: AC-16..AC-19 (остаточные замечания 002)
public class BotParityResidualTests
{
    // ---------------- AC-16 ----------------

    [Theory]
    [InlineData("demo:act:1")]
    [InlineData("zzz:stale")]
    public async Task AC16_UpsertFailsOnCallback_AnsweredOnceWithoutTextErrorLoggedWebhook200(string data)
    {
        var registry = BotHarness.NewRegistry();
        registry.UpsertAsync(default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("db-down"));
        await using var host = await BotHarness.StartAsync(registry);

        var response = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Callback(data, queryId: "q16"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = Assert.Single(host.BotApi!.Answers());
        Assert.Equal(new BotApiMock.AnsweredCallback("q16", null, false), answer);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.FullText.Contains("db-down"));
    }

    // ---------------- AC-17 ----------------

    public static TheoryData<string> AnswerFailures() => new() { "timeout", "invalid", "generic", "http" };

    private static Exception Make(string kind) => kind switch
    {
        "timeout" => new TaskCanceledException("timeout", new TimeoutException()),
        "invalid" => new InvalidOperationException("boom"),
        "http" => new HttpRequestException("net"),
        _ => new Exception("any"),
    };

    [Theory]
    [MemberData(nameof(AnswerFailures))]
    public async Task AC17_AnswerThrowsAnything_NotPropagatedWarningLogged_FromPlatform(string kind)
    {
        await using var host = await BotHarness.StartAsync(BotHarness.NewRegistry());
        host.BotApi!.AnswerCallbackQueryAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(Make(kind));

        var ex = await Record.ExceptionAsync(() => host.DispatchAsync(TestUpdates.Callback("demo:act:1")));

        Assert.Null(ex);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [MemberData(nameof(AnswerFailures))]
    public async Task AC17_AnswerThrowsAnything_NotPropagatedWarningLogged_FromHandlerAnswerAsync(string kind)
    {
        await using var host = await BotHarness.StartAsync(BotHarness.NewRegistry());
        host.BotApi!.AnswerCallbackQueryAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(Make(kind));

        var ex = await Record.ExceptionAsync(() => host.DispatchAsync(TestUpdates.Callback("demo:answer:1")));

        Assert.Null(ex);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AC17_CancelledProcessingToken_OperationCanceledStillPropagates()
    {
        await using var host = await BotHarness.StartAsync(BotHarness.NewRegistry());
        using var cts = new CancellationTokenSource();
        host.BotApi!.AnswerCallbackQueryAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(_ =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.DispatchAsync(TestUpdates.Callback("demo:act:1"), cts.Token));
    }

    // ---------------- AC-18 ----------------

    [Theory]
    [InlineData("Demo:act")]
    [InlineData("demo:Act")]
    [InlineData("demo:a-b")]
    [InlineData("d:act")]
    [InlineData("demo:abcdefghijklmnopqrstuvwxyzabcdefg")]            // action 33
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg:act")]             // module 33
    [InlineData("1demo:act")]
    [InlineData("demo:act b")]
    public void AC18_TryParse_InvalidFormat_ReturnsFalse(string data) =>
        Assert.False(CallbackData.TryParse(data, out _, out _, out _));

    [Theory]
    [InlineData("demo:act", "demo", "act", "")]
    [InlineData("demo:act:Any Payload:with:colons", "demo", "act", "Any Payload:with:colons")]
    [InlineData("ab:a", "ab", "a", "")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdef:abcdefghijklmnopqrstuvwxyzabcdef", "abcdefghijklmnopqrstuvwxyzabcdef", "abcdefghijklmnopqrstuvwxyzabcdef", "")]
    public void AC18_TryParse_ValidFormat_Parsed(string data, string module, string action, string payload)
    {
        Assert.True(CallbackData.TryParse(data, out var m, out var a, out var p));
        Assert.Equal((module, action, payload), (m, a, p));
    }

    [Theory]
    [InlineData("Demo:act")]
    [InlineData("demo:Act")]
    [InlineData("demo:a-b")]
    public async Task AC18_DispatcherAnswersStaleForMalformedData(string data)
    {
        await using var host = await BotHarness.StartAsync(BotHarness.NewRegistry());

        await host.DispatchAsync(TestUpdates.Callback(data, queryId: "q18"));

        Assert.Equal(new BotApiMock.AnsweredCallback("q18", TestBot.StaleButton, false), Assert.Single(host.BotApi!.Answers()));
        Assert.Empty(host.Services.GetRequiredService<BotRecorder>().Callbacks);
    }

    // ---------------- AC-19 ----------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AC19_429OnAnyCall_StubCalledExactlyOnce(int retryAfter)
    {
        var handler = new StubHttpMessageHandler();
        await using var host = await BotHarness.StartAsync(
            mockBotApi: false,
            configureServices: s => s.AddHttpClient(BotDefaults.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        var api = host.Services.GetRequiredService<IBotApi>();
        handler.Default = _ => StubHttpMessageHandler.Json((HttpStatusCode)429,
            StubHttpMessageHandler.ApiError(429, "Too Many Requests: retry after " + retryAfter, "{\"retry_after\":" + retryAfter + "}"));

        await Assert.ThrowsAnyAsync<Telegram.Bot.Exceptions.ApiRequestException>(
            () => api.SendMessageAsync(1, "x", null, false, CancellationToken.None));
        Assert.Equal(1, handler.CallCount);
        await Assert.ThrowsAnyAsync<Telegram.Bot.Exceptions.ApiRequestException>(
            () => api.EditMessageTextAsync(1, 2, "x", null, false, CancellationToken.None));
        Assert.Equal(2, handler.CallCount);
        await Assert.ThrowsAnyAsync<Telegram.Bot.Exceptions.ApiRequestException>(
            () => api.AnswerCallbackQueryAsync("q", null, false, CancellationToken.None));
        Assert.Equal(3, handler.CallCount);
    }
}
