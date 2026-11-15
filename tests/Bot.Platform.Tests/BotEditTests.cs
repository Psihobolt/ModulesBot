using System.Net;
using System.Text.Json;
using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Tests;

// Spec 003, TEST-3: AC-10..AC-12 (IMessageSender.EditAsync, BotCallbackContext.EditAsync)
public class BotEditTests
{
    private const long ChatId = 4242;
    private const int MessageId = 7;
    private const string Secret = "очень-секретный-текст-сообщения";

    private sealed record Env(DemoHost Host, IBotApi Api, IMessageSender Sender, IUserRegistry Registry);

    private static async Task<Env> StartAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? more = null)
    {
        var registry = BotHarness.NewRegistry();
        var host = await BotHarness.StartAsync(registry, settings, more);
        return new Env(host, host.BotApi!, host.Services.GetRequiredService<IMessageSender>(), registry);
    }

    private static Task<SendResult> Edit(Env env, string text = "new", OutgoingMessage? message = null, CancellationToken ct = default) =>
        env.Sender.EditAsync(ChatId, MessageId, message ?? new OutgoingMessage(text), ct);

    private static void Script(IBotApi api, params Func<Task>[] steps)
    {
        var funcs = steps.Select(s => (Func<NSubstitute.Core.CallInfo, Task>)(_ => s())).ToArray();
        api.EditMessageTextAsync(default, default, default!, default, default, default)
            .ReturnsForAnyArgs(funcs[0], funcs[1..]);
    }

    private static Func<Task> Fail(Exception ex) => () => Task.FromException(ex);

    private static Func<Task> Ok() => () => Task.CompletedTask;

    private static bool NoLogAboveDebug(DemoHost host) =>
        !host.Logs.Entries.Any(e => e.Level >= LogLevel.Information && e.Category.Contains("MessageSender", StringComparison.Ordinal));

    // ---------------- AC-10 ----------------

    [Fact]
    public async Task AC10_Success_SentWithSameMessageIdAndArgumentsUnchanged()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        var markup = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("ok", "demo:act"));

        var result = await env.Sender.EditAsync(ChatId, MessageId, new OutgoingMessage("<b>x</b>", markup, true), CancellationToken.None);

        Assert.Equal(new SendResult(SendStatus.Sent, MessageId), result);
        var edit = Assert.Single(env.Api.Edits());
        Assert.Equal(ChatId, edit.ChatId);
        Assert.Equal(MessageId, edit.MessageId);
        Assert.Equal("<b>x</b>", edit.Text);
        Assert.Same(markup, edit.ReplyMarkup);
        Assert.True(edit.Html);
        Assert.Empty(env.Api.Sent());
    }

    [Fact]
    public async Task AC10_NoMarkup_PassedAsNullAndPlainText()
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        await Edit(env, "plain");

        var edit = Assert.Single(env.Api.Edits());
        Assert.Null(edit.ReplyMarkup);
        Assert.False(edit.Html);
    }

    [Theory]
    [InlineData("reply")]
    [InlineData("remove")]
    [InlineData("force")]
    public async Task AC10_NonInlineMarkup_ThrowsArgumentExceptionAndApiNotCalled(string kind)
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        ReplyMarkup markup = kind switch
        {
            "reply" => new ReplyKeyboardMarkup(new KeyboardButton("a")),
            "remove" => new ReplyKeyboardRemove(),
            _ => new ForceReplyMarkup(),
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Edit(env, message: new OutgoingMessage("t", markup)));

        Assert.Empty(env.Api.Edits());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task AC10_BlankText_ThrowsArgumentExceptionAndApiNotCalled(string text)
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Edit(env, text));

        Assert.Empty(env.Api.Edits());
    }

    [Fact]
    public async Task AC10_TextLengthBoundary_4096Ok_4097Throws()
    {
        var env = await StartAsync();
        await using var _ = env.Host;

        Assert.Equal(SendStatus.Sent, (await Edit(env, new string('я', 4096))).Status);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Edit(env, new string('я', 4097)));

        Assert.Single(env.Api.Edits());
    }

    // ---------------- AC-11 ----------------

    [Theory]
    [InlineData("Bad Request: message is not modified: specified new message content and reply markup are exactly the same")]
    [InlineData("Bad Request: MESSAGE IS NOT MODIFIED")]
    public async Task AC11_NotModified_TreatedAsSentWithoutWarning(string description)
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.BadRequest(description)));

        var result = await Edit(env);

        Assert.Equal(new SendResult(SendStatus.Sent, MessageId), result);
        Assert.True(NoLogAboveDebug(env.Host));
    }

    [Theory]
    [InlineData("Bad Request: message to edit not found")]
    [InlineData("Bad Request: message can't be edited")]
    public async Task AC11_Other400_FailedWithWarning(string description)
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.BadRequest(description)));

        var result = await Edit(env, Secret);

        Assert.Equal(new SendResult(SendStatus.Failed, null), result);
        Assert.Contains(env.Host.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(env.Host.Logs.Entries.Where(e => e.Category.StartsWith("Bot.", StringComparison.Ordinal)),
            e => e.Message.Contains(Secret));
    }

    [Fact]
    public async Task AC11_Forbidden_BlockedAndRegistryMarked()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.Forbidden()));

        var result = await Edit(env);

        Assert.Equal(new SendResult(SendStatus.Blocked, null), result);
        await env.Registry.Received(1).MarkCannotReceiveMessagesAsync(ChatId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC11_RateLimitWithinMax_WaitsAndRetriesOnce()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(2)), Ok());

        var result = await Edit(env);

        Assert.Equal(new SendResult(SendStatus.Sent, MessageId), result);
        Assert.Equal([TimeSpan.FromSeconds(2)], env.Host.Clock.Delays);
        Assert.Equal(2, env.Api.Edits().Count);
    }

    [Fact]
    public async Task AC11_RateLimitAboveMax_RateLimitedWithoutRetry()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Script(env.Api, Fail(TestBot.TooManyRequests(60)), Ok());

        var result = await Edit(env);

        Assert.Equal(new SendResult(SendStatus.RateLimited, null), result);
        Assert.Single(env.Api.Edits());
        Assert.Empty(env.Host.Clock.Delays);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("invalid")]
    [InlineData("timeout")]
    public async Task AC11_OtherErrors_FailedWithWarning(string kind)
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        Exception ex = kind switch
        {
            "http" => new HttpRequestException("net"),
            "invalid" => new InvalidOperationException("x"),
            _ => new TaskCanceledException("timeout", new TimeoutException()),
        };
        Script(env.Api, Fail(ex));

        var result = await Edit(env);

        Assert.Equal(SendStatus.Failed, result.Status);
        Assert.Contains(env.Host.Logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AC11_CancelledToken_ThrowsOperationCanceled()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Edit(env, ct: cts.Token));
    }

    [Fact]
    public async Task AC11_CancelledDuringApiCall_ThrowsOperationCanceled()
    {
        var env = await StartAsync();
        await using var _ = env.Host;
        using var cts = new CancellationTokenSource();
        Script(env.Api, () =>
        {
            cts.Cancel();
            return Task.FromCanceled(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Edit(env, ct: cts.Token));
    }

    // ---------------- AC-12 ----------------

    [Fact]
    public async Task AC12_CallbackFromMessage7_ContextEditsSameMessage()
    {
        await using var host = await BotHarness.StartAsync(BotHarness.NewRegistry());

        await host.DispatchAsync(TestUpdates.Callback("demo:edit", userId: 42, messageId: 7));

        var ctx = Assert.Single(host.Services.GetRequiredService<BotRecorder>().Callbacks);
        Assert.Equal(7, ctx.MessageId);
        var edit = Assert.Single(host.BotApi!.Edits());
        Assert.Equal((42L, 7, "edited"), (edit.ChatId, edit.MessageId, edit.Text));
        Assert.Empty(host.BotApi!.Sent());
    }

    [Fact]
    public async Task AC12_CallbackWithoutSourceMessage_EditThrowsInvalidOperationAndSenderNotCalled()
    {
        var sender = Substitute.For<IMessageSender>();
        var ctx = new BotCallbackContext
        {
            Update = new Telegram.Bot.Types.Update(),
            ChatId = ChatId,
            From = new Bot.Platform.Auth.TelegramUser(42, "Иван", null, null, null, false, null),
            Services = Substitute.For<IServiceProvider>(),
            Sender = sender,
            CancellationToken = CancellationToken.None,
            Query = new Telegram.Bot.Types.CallbackQuery { Id = "cq", Data = "demo:edit" },
            Action = "edit",
            Payload = "",
        };

        Assert.Null(ctx.MessageId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.EditAsync(new OutgoingMessage("x")));
        await sender.DidNotReceiveWithAnyArgs().EditAsync(default, default, default!, default);
    }

    // ---------------- real adapter over stub HTTP ----------------

    private static async Task<(DemoHost Host, StubHttpMessageHandler Handler)> StartRealAsync()
    {
        var handler = new StubHttpMessageHandler();
        var host = await BotHarness.StartAsync(
            mockBotApi: false,
            configureServices: s => s.AddHttpClient(BotDefaults.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        return (host, handler);
    }

    [Fact]
    public async Task AC10_RealAdapter_PostsEditMessageTextWithIdsHtmlAndMarkup()
    {
        var (host, handler) = await StartRealAsync();
        await using var _ = host;
        handler.Enqueue(HttpStatusCode.OK, StubHttpMessageHandler.OkMessage(7));
        var markup = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("ok", "demo:act"));

        var result = await host.Services.GetRequiredService<IMessageSender>()
            .EditAsync(ChatId, MessageId, new OutgoingMessage("<b>x</b>", markup, true), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/editMessageText", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(ChatId, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal(MessageId, body.RootElement.GetProperty("message_id").GetInt32());
        Assert.Equal("<b>x</b>", body.RootElement.GetProperty("text").GetString());
        Assert.Equal("Html", body.RootElement.GetProperty("parse_mode").GetString(), ignoreCase: true);
        Assert.Contains("demo:act", request.Body);
    }

    [Fact]
    public async Task AC11_RealAdapter_NotModified400_IsSent()
    {
        var (host, handler) = await StartRealAsync();
        await using var _ = host;
        handler.Default = _ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest,
            StubHttpMessageHandler.ApiError(400, "Bad Request: message is not modified: specified new message content and reply markup are exactly the same"));

        var result = await host.Services.GetRequiredService<IMessageSender>()
            .EditAsync(ChatId, MessageId, new OutgoingMessage("same"), CancellationToken.None);

        Assert.Equal(new SendResult(SendStatus.Sent, MessageId), result);
        Assert.Equal(1, handler.CallCount);
    }
}
