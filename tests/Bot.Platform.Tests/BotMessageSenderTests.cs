using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Tests;

// TEST-5: AC-14..AC-18, AC-15 registry calls
public class BotMessageSenderTests
{
    private const long ChatId = 4242;
    private const string SecretText = "очень-секретный-текст-сообщения";

    private sealed record Env(DemoHost Host, IBotApi Api, IMessageSender Sender, IUserRegistry Registry)
    {
        public FakeClock Clock => Host.Clock;
    }

    private static async Task<Env> StartAsync(
        Dictionary<string, string?>? settings = null,
        IUserRegistry? registry = null,
        Action<IServiceCollection>? more = null)
    {
        registry ??= BotHarness.NewRegistry();
        var host = await BotHarness.StartAsync(registry, settings, more);
        return new Env(host, host.BotApi!, host.Services.GetRequiredService<IMessageSender>(), registry);
    }

    private static void Script(IBotApi api, params Func<Task<int>>[] steps)
    {
        var funcs = steps.Select(s => (Func<NSubstitute.Core.CallInfo, Task<int>>)(_ => s())).ToArray();
        api.SendMessageAsync(default, default!, default, default, default)
            .ReturnsForAnyArgs(funcs[0], funcs[1..]);
    }

    private static Func<Task<int>> Fail(Exception ex) => () => Task.FromException<int>(ex);

    private static Func<Task<int>> Ok(int id) => () => Task.FromResult(id);

    private static Task<SendResult> Send(Env env, string text = "hi", CancellationToken ct = default) =>
        env.Sender.SendAsync(ChatId, new OutgoingMessage(text), ct);

    private static int Calls(IBotApi api) => api.Sent().Count;

    // ---------------- AC-14 ----------------

    [Fact]
    public async Task AC14_ApiReturnsMessageId_SentWithIdAndArgumentsPassedUnchanged()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Ok(77));
        var markup = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("ok", "demo:act"));

        var result = await env.Sender.SendAsync(ChatId, new OutgoingMessage("<b>hi</b>", markup, true), CancellationToken.None);

        Assert.Equal(new SendResult(SendStatus.Sent, 77), result);
        var sent = Assert.Single(env.Api.Sent());
        Assert.Equal(ChatId, sent.ChatId);
        Assert.Equal("<b>hi</b>", sent.Text);
        Assert.Same(markup, sent.ReplyMarkup);
        Assert.True(sent.Html);
    }

    [Fact]
    public async Task AC14_DefaultMessage_NoMarkupNoHtml()
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        var result = await Send(env, "plain");

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.NotNull(result.MessageId);
        var sent = Assert.Single(env.Api.Sent());
        Assert.Null(sent.ReplyMarkup);
        Assert.False(sent.Html);
    }

    // ---------------- AC-15 ----------------

    [Fact]
    public async Task AC15_Forbidden_BlockedWithoutExceptionAndRegistryMarked()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.Forbidden()));

        var result = await Send(env, SecretText);

        Assert.Equal(new SendResult(SendStatus.Blocked, null), result);
        await env.Registry.Received(1).MarkCannotReceiveMessagesAsync(ChatId, Arg.Any<CancellationToken>());
        var info = Assert.Single(env.Host.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains(ChatId.ToString()));
        Assert.DoesNotContain(SecretText, info.FullText);
        Assert.DoesNotContain(env.Host.Logs.Entries, e => e.FullText.Contains(SecretText));
    }

    [Fact]
    public async Task AC15_MarkCannotReceiveFails_WarningLoggedAndStillBlocked()
    {
        var registry = BotHarness.NewRegistry();
        registry.MarkCannotReceiveMessagesAsync(default, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db-down"));
        var env = await StartAsync(registry: registry);
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.Forbidden()));

        var result = await Send(env);

        Assert.Equal(SendStatus.Blocked, result.Status);
        Assert.Contains(env.Host.Logs.Entries, e => e.Level == LogLevel.Warning && e.FullText.Contains("db-down"));
    }

    // ---------------- AC-16 ----------------

    [Fact]
    public async Task AC16_RetryAfterWithinLimit_WaitsAndRetriesOnce_Sent()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(3)), Ok(5));

        var result = await Send(env);

        Assert.Equal(new SendResult(SendStatus.Sent, 5), result);
        Assert.Equal([TimeSpan.FromSeconds(3)], env.Clock.Delays);
        Assert.Equal(2, Calls(env.Api));
    }

    [Fact]
    public async Task AC16_RetryAfterEqualsMaxRetryAfter_StillWaits()
    {
        var env = await StartAsync(new() { ["Bot:MaxRetryAfter"] = "00:00:05" });
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(5)), Ok(6));

        var result = await Send(env);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal([TimeSpan.FromSeconds(5)], env.Clock.Delays);
    }

    [Fact]
    public async Task AC16_RetryAfterAboveMax_RateLimitedWithoutWaitingOrRetry()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(6)), Ok(1));

        var result = await Send(env);

        Assert.Equal(new SendResult(SendStatus.RateLimited, null), result);
        Assert.Empty(env.Clock.Delays);
        Assert.Equal(1, Calls(env.Api));
    }

    [Fact]
    public async Task AC16_CustomMaxRetryAfter_IsRespected()
    {
        var env = await StartAsync(new() { ["Bot:MaxRetryAfter"] = "00:00:10" });
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(10)), Ok(1));

        Assert.Equal(SendStatus.Sent, (await Send(env)).Status);
        Assert.Equal([TimeSpan.FromSeconds(10)], env.Clock.Delays);
    }

    [Fact]
    public async Task AC16_RetryAfterMissing_RateLimitedImmediately()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(null)), Ok(1));

        var result = await Send(env);

        Assert.Equal(SendStatus.RateLimited, result.Status);
        Assert.Empty(env.Clock.Delays);
        Assert.Equal(1, Calls(env.Api));
    }

    [Fact]
    public async Task AC16_RetryAlsoRateLimited_RateLimitedWithSingleWait()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(2)), Fail(TestBot.TooManyRequests(2)), Ok(1));

        var result = await Send(env);

        Assert.Equal(SendStatus.RateLimited, result.Status);
        Assert.Single(env.Clock.Delays);
        Assert.Equal(2, Calls(env.Api));
    }

    [Fact]
    public async Task AC16_RetryForbidden_BlockedAndRegistryMarked()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(1)), Fail(TestBot.Forbidden()));

        var result = await Send(env);

        Assert.Equal(SendStatus.Blocked, result.Status);
        await env.Registry.Received(1).MarkCannotReceiveMessagesAsync(ChatId, Arg.Any<CancellationToken>());
        Assert.Equal(2, Calls(env.Api));
    }

    // ---------------- AC-17 ----------------

    public static TheoryData<string> OtherErrors() => new("api400", "api500", "request", "http", "timeout");

    private static Exception MakeError(string kind) => kind switch
    {
        "api400" => TestBot.BadRequest("Bad Request: chat not found"),
        "api500" => new ApiRequestException("Internal", 500),
        "request" => new RequestException("network"),
        "http" => new HttpRequestException("connection refused"),
        "timeout" => new TaskCanceledException("timeout", new TimeoutException()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(OtherErrors))]
    public async Task AC17_OtherError_FailedWithWarningAndNoException(string kind)
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(MakeError(kind)));

        var result = await Send(env, SecretText);

        Assert.Equal(new SendResult(SendStatus.Failed, null), result);
        Assert.Contains(env.Host.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(env.Host.Logs.Entries.Where(e => e.Level >= LogLevel.Information && e.Category.StartsWith("Bot.")),
            e => e.Message.Contains(SecretText));
        await env.Registry.DidNotReceiveWithAnyArgs().MarkCannotReceiveMessagesAsync(default, default);
    }

    [Fact]
    public async Task AC17_CancelledBeforeCall_ThrowsOperationCanceled()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(env, ct: cts.Token));
    }

    [Fact]
    public async Task AC17_CancelledDuringApiCall_ThrowsOperationCanceled()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        using var cts = new CancellationTokenSource();
        Script(env.Api, () =>
        {
            cts.Cancel();
            return Task.FromCanceled<int>(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(env, ct: cts.Token));
    }

    [Fact]
    public async Task AC17_CancelledDuringDelay_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var clock = Substitute.For<IClock>();
        clock.DelayAsync(default, default).ReturnsForAnyArgs(_ =>
        {
            cts.Cancel();
            return Task.FromCanceled(cts.Token);
        });
        var env = await StartAsync(more: s => s.AddSingleton(clock));
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(1)), Ok(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(env, ct: cts.Token));
        Assert.Equal(1, Calls(env.Api));
    }

    // ---------------- AC-18 ----------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  \n\t ")]
    public async Task AC18_EmptyOrWhitespaceText_ThrowsArgumentExceptionAndApiNotCalled(string text)
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Send(env, text));

        Assert.Equal(0, Calls(env.Api));
    }

    [Fact]
    public async Task AC18_Text4097Chars_ThrowsArgumentExceptionAndApiNotCalled()
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Send(env, new string('я', 4097)));

        Assert.Equal(0, Calls(env.Api));
    }

    [Fact]
    public async Task AC18_Text4096Chars_Sent()
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        var result = await Send(env, new string('я', 4096));

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(4096, Assert.Single(env.Api.Sent()).Text.Length);
    }
}
