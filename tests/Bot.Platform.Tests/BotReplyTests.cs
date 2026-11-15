using System.Net;
using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Bot.Platform.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Bot.Platform.Tests;

// Spec 003, TEST-2: AC-1..AC-9 (ожидание текстового ответа) + регистрация MapReply (AC-6).
public class BotReplyTests
{
    private static Task<DemoHost> StartAsync(Dictionary<string, string?>? settings = null) =>
        BotHarness.StartAsync(BotHarness.NewRegistry(), settings);

    private static BotRecorder Recorder(DemoHost h) => h.Services.GetRequiredService<BotRecorder>();

    private static IReadOnlyList<BotApiMock.SentMessage> Sent(DemoHost h) => h.BotApi!.Sent();

    // ---------------- AC-1 ----------------

    [Fact]
    public async Task AC1_PendingReplyAndPlainText_HandlerCalledOnceWithTrimmedText()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "name", "p1");

        await host.DispatchAsync(TestUpdates.Private("  Киноклуб  ", userId: 42));

        var ctx = Assert.Single(Recorder(host).Replies);
        Assert.Equal("name", ctx.Action);
        Assert.Equal("p1", ctx.Payload);
        Assert.Equal("Киноклуб", ctx.Text);
        Assert.Equal(42, ctx.ChatId);
        Assert.Equal(42, ctx.From.Id);
        Assert.Equal("  Киноклуб  ", ctx.Message.Text);
        Assert.Empty(Sent(host));
        Assert.Empty(host.PendingRows(42));
    }

    [Fact]
    public async Task AC1_SecondTextAfterConsumption_NotACommandAndHandlerNotCalledAgain()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Private("one", userId: 42));
        await host.DispatchAsync(TestUpdates.Private("two", userId: 42));

        Assert.Single(Recorder(host).Replies);
        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
    }

    [Fact]
    public async Task AC1_HandlerExpectsAgain_NewPendingIsKept()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "again", "p0");

        await host.DispatchAsync(TestUpdates.Private("x", userId: 42));

        var row = Assert.Single(host.PendingRows(42));
        Assert.Equal(new BotHarness.PendingRow(42, "demo", "name", "p2"), row);
        await host.DispatchAsync(TestUpdates.Private("y", userId: 42));
        Assert.Equal(["again", "name"], Recorder(host).Replies.Select(r => r.Action).ToArray());
    }

    [Fact]
    public async Task AC1_MultilineText_OnlyEdgesTrimmed()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Private("\n a\n b \t", userId: 42));

        Assert.Equal("a\n b", Assert.Single(Recorder(host).Replies).Text);
    }

    [Fact]
    public async Task AC1_PendingInGroupChat_IgnoredAndKept()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Group("hello", userId: 42));

        Assert.Empty(Recorder(host).Replies);
        Assert.Single(host.PendingRows(42));
        Assert.Empty(Sent(host));
    }

    // ---------------- AC-2 ----------------

    [Theory]
    [InlineData("/help")]
    [InlineData("/start")]
    [InlineData("/start demo_x")]
    [InlineData("/nope")]
    [InlineData("/ping")]
    [InlineData("/")]
    [InlineData("/HELP@AnyBot")]
    public async Task AC2_AnyCommand_CancelsPendingAndHandlerNotCalled(string text)
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Private(text, userId: 42));

        Assert.Empty(host.PendingRows(42));
        Assert.Empty(Recorder(host).Replies);
        await host.DispatchAsync(TestUpdates.Private("plain", userId: 42));
        Assert.Empty(Recorder(host).Replies);
        Assert.Equal(TestBot.NotACommand, Sent(host).Last().Text);
    }

    [Fact]
    public async Task AC2_ModuleCommand_StillExecutesAfterCancel()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Command("ping", userId: 42));

        Assert.Single(Recorder(host).Commands);
        Assert.Empty(host.PendingRows(42));
    }

    [Fact]
    public async Task AC2_HelpCommand_WorksAsUsual()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Command("help", userId: 42));

        Assert.StartsWith("Команды:", Assert.Single(Sent(host)).Text);
    }

    // ---------------- AC-3 ----------------

    [Fact]
    public async Task AC3_ExactlyAtExpiresAt_Expired()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);
        host.Clock.Advance(TimeSpan.FromMinutes(10));

        await host.DispatchAsync(TestUpdates.Private("late", userId: 42));

        Assert.Empty(Recorder(host).Replies);
        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
        Assert.Empty(host.PendingRows(42));
    }

    [Fact]
    public async Task AC3_OneTickBeforeExpiry_StillActive()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);
        host.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));

        await host.DispatchAsync(TestUpdates.Private("in time", userId: 42));

        Assert.Single(Recorder(host).Replies);
        Assert.Empty(Sent(host));
    }

    [Fact]
    public async Task AC3_CustomReplyTimeout_IsUsed()
    {
        await using var host = await StartAsync(new() { ["Bot:ReplyTimeout"] = "00:01:00" });
        await host.ExpectAsync(42);
        host.Clock.Advance(TimeSpan.FromMinutes(1));

        await host.DispatchAsync(TestUpdates.Private("late", userId: 42));

        Assert.Empty(Recorder(host).Replies);
        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
    }

    [Fact]
    public async Task AC3_ExpectRestartsTimer_UsesTimeOfLastCall()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);
        host.Clock.Advance(TimeSpan.FromMinutes(9));
        await host.ExpectAsync(42);
        host.Clock.Advance(TimeSpan.FromMinutes(9));

        await host.DispatchAsync(TestUpdates.Private("x", userId: 42));

        Assert.Single(Recorder(host).Replies);
    }

    // ---------------- AC-4 ----------------

    [Fact]
    public async Task AC4_StickerWithPending_AsksForTextAndKeepsPending()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.Sticker(userId: 42));

        Assert.Equal(TestBot.SendTextOnly, Assert.Single(Sent(host)).Text);
        Assert.Single(host.PendingRows(42));
        Assert.Empty(Recorder(host).Replies);
        await host.DispatchAsync(TestUpdates.Private("ok", userId: 42));
        Assert.Single(Recorder(host).Replies);
    }

    [Fact]
    public async Task AC4_PhotoWithCaptionWithPending_AsksForTextAndCaptionIsNotUsed()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        await host.DispatchAsync(TestUpdates.PhotoWithCaption("caption", userId: 42));

        Assert.Equal(TestBot.SendTextOnly, Assert.Single(Sent(host)).Text);
        Assert.Single(host.PendingRows(42));
        Assert.Empty(Recorder(host).Replies);
    }

    [Fact]
    public async Task AC4_StickerWithoutPending_BehavesLike002()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Sticker(userId: 42));

        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
    }

    // ---------------- AC-5 ----------------

    [Fact]
    public async Task AC5_SecondExpect_ReplacesFirstAndNullPayloadBecomesEmpty()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "name", "p1");

        await host.ExpectAsync(42, "other", null);

        var row = Assert.Single(host.PendingRows(42));
        Assert.Equal(new BotHarness.PendingRow(42, "demo", "other", ""), row);
        await host.DispatchAsync(TestUpdates.Private("t", userId: 42));
        var ctx = Assert.Single(Recorder(host).Replies);
        Assert.Equal("other", ctx.Action);
        Assert.Equal("", ctx.Payload);
    }

    [Fact]
    public async Task AC5_Cancel_TrueThenFalse()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);

        Assert.True(await host.CancelPendingAsync(42));
        Assert.Empty(host.PendingRows(42));
        Assert.False(await host.CancelPendingAsync(42));
    }

    [Fact]
    public async Task AC5_CancelWithoutPending_False()
    {
        await using var host = await StartAsync();
        Assert.False(await host.CancelPendingAsync(999));
    }

    [Fact]
    public async Task AC5_UsersAreIndependent()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(1, "name", "a");
        await host.ExpectAsync(2, "other", "b");

        await host.DispatchAsync(TestUpdates.Private("from stranger", userId: 3));
        Assert.True(await host.CancelPendingAsync(1));
        await host.DispatchAsync(TestUpdates.Private("hi", userId: 2));

        var ctx = Assert.Single(Recorder(host).Replies);
        Assert.Equal((2L, "other", "b"), (ctx.From.Id, ctx.Action, ctx.Payload));
        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);   // user 3 has no pending
        Assert.Empty(host.PendingRows());
    }

    // ---------------- AC-6 ----------------

    [Fact]
    public async Task AC6_UnknownModule_ThrowsArgumentException()
    {
        await using var host = await StartAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.ExpectAsync(42, "name", "p", module: "nomod"));
        Assert.Empty(host.PendingRows());
    }

    [Fact]
    public async Task AC6_UnknownAction_ThrowsArgumentException()
    {
        await using var host = await StartAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.ExpectAsync(42, "missing", "p"));
        Assert.Empty(host.PendingRows());
    }

    [Fact]
    public async Task AC6_ActionRegisteredAsCallbackOnly_ThrowsArgumentException()
    {
        await using var host = await StartAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.ExpectAsync(42, "act", "p"));
    }

    [Fact]
    public async Task AC6_PayloadBoundary_256Ok_257Throws()
    {
        await using var host = await StartAsync();

        await host.ExpectAsync(42, "name", new string('p', 256));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.ExpectAsync(43, "name", new string('p', 257)));

        Assert.Single(host.PendingRows());
    }

    [Fact]
    public async Task AC6_FailedExpect_DoesNotTouchExistingPending()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "name", "keep");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.ExpectAsync(42, "missing", "x"));

        Assert.Equal("keep", Assert.Single(host.PendingRows(42)).Payload);
    }

    private static readonly BotHandler<BotReplyContext> NoopReply = _ => Task.CompletedTask;

    private static InvalidOperationException Reject(Action<IBotRouteBuilder> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Bot:Token"] = TestInitData.BotToken;
        builder.Configuration["Bot:WebhookSecret"] = TestBot.WebhookSecret;
        builder.AddPlatform();
        using var _ = ConfigurableModule.Use("mod", map);
        return Assert.Throws<InvalidOperationException>(() => builder.AddModule<ConfigurableModule>());
    }

    private static void Accept(Action<IBotRouteBuilder> map, string module = "mod")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Bot:Token"] = TestInitData.BotToken;
        builder.Configuration["Bot:WebhookSecret"] = TestBot.WebhookSecret;
        builder.AddPlatform();
        using var _ = ConfigurableModule.Use(module, map);
        builder.AddModule<ConfigurableModule>();
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("имя")]
    [InlineData("a-b")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public void AC6_InvalidReplyActionFormat_ThrowsNamingModuleAndAction(string action)
    {
        var ex = Reject(b => b.MapReply(action, NoopReply));

        Assert.Contains("mod", ex.Message);
        Assert.Contains(action, ex.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("name_2")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdef")]
    public void AC6_ValidReplyActionFormat_Accepted(string action) => Accept(b => b.MapReply(action, NoopReply));

    [Fact]
    public void AC6_DuplicateReplyActionInModule_Throws()
    {
        var ex = Reject(b =>
        {
            b.MapReply("dup", NoopReply);
            b.MapReply("dup", NoopReply);
        });

        Assert.Contains("mod", ex.Message);
        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void AC6_NullReplyHandler_Throws() => Reject(b => b.MapReply("act", null!));

    // ---------------- AC-7 ----------------

    [Fact]
    public async Task AC7_TwoParallelTexts_HandlerCalledExactlyOnceAndLoserGetsNotACommand()
    {
        await using var host = await StartAsync();
        const int users = 12;
        for (var u = 1; u <= users; u++) await host.ExpectAsync(u);

        await Task.WhenAll(Enumerable.Range(1, users).SelectMany(u => new[]
        {
            host.DispatchAsync(TestUpdates.Private("first", userId: u)),
            host.DispatchAsync(TestUpdates.Private("second", userId: u)),
        }));

        for (var u = 1; u <= users; u++)
        {
            Assert.Equal(1, Recorder(host).Replies.Count(r => r.From.Id == u));
            Assert.Equal(1, Sent(host).Count(m => m.ChatId == u && m.Text == TestBot.NotACommand));
        }

        Assert.Empty(host.PendingRows());
    }

    /// <summary>
    /// Parks the first DELETE of platform_pending_replies right before execution (after the consumer already
    /// read the row) until released; later DELETEs pass through.
    /// </summary>
    private sealed class ParkFirstDelete : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int _seen;

        public TaskCompletionSource Parked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("platform_pending_replies", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref _seen) == 1)
            {
                Parked.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    [Fact]
    public async Task AC7_HandlerOfFirstMessageExpectsAgainWhileSecondIsInFlight_HandlerCalledOnceAndNewPendingKept()
    {
        var park = new ParkFirstDelete();
        await using var racing = await BotHarness.StartAsync(
            BotHarness.NewRegistry(),
            configureServices: services => services.ConfigureDbContext<Bot.Platform.Users.PlatformDbContext>(
                o => o.AddInterceptors(park)));
        await racing.ExpectAsync(42, "again", "p0");

        // T2 reads the pending row and parks right before deleting it.
        var second = racing.DispatchAsync(TestUpdates.Private("second", userId: 42));
        await park.Parked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // T1 consumes the same row, its handler ("again") puts a NEW expectation.
        await racing.DispatchAsync(TestUpdates.Private("first", userId: 42));
        var row = Assert.Single(racing.PendingRows(42));
        Assert.Equal(new BotHarness.PendingRow(42, "demo", "name", "p2"), row);

        park.Release.SetResult();
        await second;

        Assert.Equal(["again"], Recorder(racing).Replies.Select(r => r.Action).ToArray());
        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(racing)).Text);
        Assert.Equal(new BotHarness.PendingRow(42, "demo", "name", "p2"), Assert.Single(racing.PendingRows(42)));
    }

    // ---------------- AC-7a ----------------

    [Theory]
    [InlineData("   ")]
    [InlineData("\n\t  \n")]
    public async Task AC7a_WhitespaceOnlyWithPending_AsksForTextKeepsPendingHandlerNotCalled(string text)
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "name", "p1");

        await host.DispatchAsync(TestUpdates.Private(text, userId: 42));

        Assert.Equal(TestBot.SendTextOnly, Assert.Single(Sent(host)).Text);
        Assert.Empty(Recorder(host).Replies);
        Assert.Equal(new BotHarness.PendingRow(42, "demo", "name", "p1"), Assert.Single(host.PendingRows(42)));
        await host.DispatchAsync(TestUpdates.Private("real", userId: 42));
        Assert.Equal("real", Assert.Single(Recorder(host).Replies).Text);
    }

    [Fact]
    public async Task AC7a_WhitespaceOnlyWithoutPending_NotACommand()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private("   ", userId: 42));

        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
        Assert.Empty(Recorder(host).Replies);
    }

    // ---------------- AC-6 (resolution (c)) ----------------

    [Fact]
    public void AC6_SameActionForReplyAndCallbackInOneModule_Accepted() =>
        Accept(b =>
        {
            b.MapReply("x", NoopReply);
            b.MapCallback("x", _ => Task.CompletedTask);
        });

    [Fact]
    public async Task AC6_SameActionForReplyAndCallback_BothWork()
    {
        // DemoModule registers "boom" both as callback and as reply.
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "boom");

        // Both handlers throw by design; the webhook swallows it (200).
        var cb = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Callback("demo:boom", userId: 7));
        var rp = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Private("txt", userId: 42));
        Assert.Equal(HttpStatusCode.OK, cb.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rp.StatusCode);

        Assert.Single(Recorder(host).Callbacks);
        Assert.Single(Recorder(host).Replies);
    }

    // ---------------- AC-8 ----------------

    [Fact]
    public async Task AC8_ActionNoLongerRegistered_PendingDeletedAndNotACommandWithoutException()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42);
        host.RenamePendingAction(42, "gone");

        var ex = await Record.ExceptionAsync(() => host.DispatchAsync(TestUpdates.Private("hi", userId: 42)));

        Assert.Null(ex);
        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
        Assert.Empty(host.PendingRows(42));
        Assert.Empty(Recorder(host).Replies);
    }

    // ---------------- AC-9 ----------------

    [Fact]
    public async Task AC9_HandlerThrows_Webhook200PendingRemovedErrorLogged()
    {
        await using var host = await StartAsync();
        await host.ExpectAsync(42, "boom");

        var response = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Private("kaboom", userId: 42));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.PendingRows(42));
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.FullText.Contains("secret-boom-detail"));
        await host.DispatchAsync(TestUpdates.Private("again", userId: 42));
        Assert.Single(Recorder(host).Replies);
    }
}
