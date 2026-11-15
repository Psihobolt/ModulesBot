using System.Net;
using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Users;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Bot.Platform.Tests;

// TEST-2: AC-1..AC-4 (webhook over HTTP)
public class BotWebhookTests
{
    private const string ValidUpdate = "{\"update_id\":1,\"message\":{\"message_id\":1,\"date\":1700000000,\"chat\":{\"id\":1001,\"type\":\"private\"},\"from\":{\"id\":1001,\"is_bot\":false,\"first_name\":\"A\"},\"text\":\"/start\"}}";

    private static async Task<(DemoHost Host, IUpdateDispatcher Dispatcher)> StartWithSubstituteDispatcherAsync()
    {
        var dispatcher = Substitute.For<IUpdateDispatcher>();
        dispatcher.DispatchAsync(default!, default).ReturnsForAnyArgs(Task.CompletedTask);
        var host = await BotHarness.StartAsync(configureServices: s => s.AddSingleton(dispatcher));
        return (host, dispatcher);
    }

    private static async Task AssertEmptyBodyAsync(HttpResponseMessage response) =>
        Assert.Equal("", await response.Content.ReadAsStringAsync());

    // ---------------- AC-1 ----------------

    [Theory]
    [InlineData(null)]                                     // no header
    [InlineData("")]                                       // empty
    [InlineData("wrong-secret")]                           // wrong, other length
    [InlineData("test-webhook-secreT")]                    // same length, differs in one char
    [InlineData("test-webhook-secre")]                     // prefix (shorter)
    [InlineData("test-webhook-secret-and-more")]           // longer
    [InlineData("TEST-WEBHOOK-SECRET")]                    // case differs
    public async Task AC1_MissingEmptyOrWrongSecret_Returns401EmptyBodyAndDispatcherNotCalled(string? secret)
    {
        var (host, dispatcher) = await StartWithSubstituteDispatcherAsync();
        await using var _ = host;

        var response = await BotHarness.PostWebhookAsync(host.Client, ValidUpdate, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEmptyBodyAsync(response);
        await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task AC1_WrongSecretAndBrokenBody_Returns401NotBadRequest(string body)
    {
        var (host, dispatcher) = await StartWithSubstituteDispatcherAsync();
        await using var _ = host;

        var response = await BotHarness.PostWebhookAsync(host.Client, body, "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEmptyBodyAsync(response);
        await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task AC1_NoSecretHeaderAndBrokenBody_Returns401()
    {
        var (host, _) = await StartWithSubstituteDispatcherAsync();
        await using var __ = host;

        var response = await BotHarness.PostWebhookAsync(host.Client, "{not json", secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------- AC-2 ----------------

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[1,2")]
    public async Task AC2_ValidSecretAndBadBody_Returns400EmptyBodyAndDispatcherNotCalled(string body)
    {
        var (host, dispatcher) = await StartWithSubstituteDispatcherAsync();
        await using var _ = host;

        var response = await BotHarness.PostWebhookAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEmptyBodyAsync(response);
        await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    // ---------------- AC-3 ----------------

    [Fact]
    public async Task AC3_ValidUpdate_Returns200EmptyBodyAndDispatcherCalledOnceWithParsedUpdate()
    {
        var (host, dispatcher) = await StartWithSubstituteDispatcherAsync();
        await using var _ = host;

        var response = await BotHarness.PostWebhookAsync(host.Client, ValidUpdate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertEmptyBodyAsync(response);
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<Telegram.Bot.Types.Update>(u => u.Id == 1 && u.Message!.Text == "/start" && u.Message.From!.Id == 1001),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_ResponseIsSentOnlyAfterDispatchCompleted()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Substitute.For<IUpdateDispatcher>();
        dispatcher.DispatchAsync(default!, default).ReturnsForAnyArgs(gate.Task);
        await using var host = await BotHarness.StartAsync(configureServices: s => s.AddSingleton(dispatcher));

        var pending = BotHarness.PostWebhookAsync(host.Client, ValidUpdate);
        await Task.Delay(300);
        Assert.False(pending.IsCompleted, "response must wait for DispatchAsync");

        gate.SetResult();
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC3_ModuleHandlerThrows_Returns200LogsErrorAndNextUpdateIsProcessed()
    {
        await using var host = await BotHarness.StartAsync();

        var bomb = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Command("ping", "boom"));
        var next = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start());

        Assert.Equal(HttpStatusCode.OK, bomb.StatusCode);
        await AssertEmptyBodyAsync(bomb);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.FullText.Contains("secret-boom-detail"));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Contains(host.BotApi!.Sent(), m => m.Text == TestBot.Welcome);
    }

    [Fact]
    public async Task AC3_UserUpsertThrows_Returns200LogsErrorAndNextUpdateIsProcessed()
    {
        var registry = BotHarness.NewRegistry();
        registry.UpsertAsync(default!, default, default).ReturnsForAnyArgs(
            _ => Task.FromException(new InvalidOperationException("upsert-failed-detail")),
            _ => Task.CompletedTask);
        await using var host = await BotHarness.StartAsync(registry);

        var first = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start());
        var second = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.FullText.Contains("upsert-failed-detail"));
        Assert.Contains(host.BotApi!.Sent(), m => m.Text == TestBot.Welcome);
    }

    [Fact]
    public async Task AC3_TelegramErrorWhileReplying_Returns200()
    {
        await using var host = await BotHarness.StartAsync();
        host.BotApi!.SendMessageAsync(default, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(TestBot.Forbidden());

        var response = await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC3_GetWebhook_Returns404Or405NotIndexHtml()
    {
        using var factory = new PlatformFactory { DistPath = CreateDistWithIndex(out var dir) };
        try
        {
            var response = await factory.CreateClient().GetAsync("/bot/webhook");

            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, response.StatusCode.ToString());
            Assert.DoesNotContain("SPA-INDEX", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            TempDir.TryDelete(dir);
        }
    }

    [Fact]
    public async Task AC3_PostOtherBotPath_Returns404Or405NotIndexHtml()
    {
        using var factory = new PlatformFactory { DistPath = CreateDistWithIndex(out var dir) };
        try
        {
            var response = await BotHarness.PostWebhookAsync(factory.CreateClient(), ValidUpdate, TestBot.WebhookSecret, "/bot/other");

            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, response.StatusCode.ToString());
            Assert.DoesNotContain("SPA-INDEX", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            TempDir.TryDelete(dir);
        }
    }

    [Fact]
    public async Task AC3_RealProgramHost_WebhookWithSecretWorksWithoutPublicUrl()
    {
        using var factory = new PlatformFactory();

        var response = await BotHarness.PostWebhookAsync(factory.CreateClient(), TestUpdates.InlineQuery());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string CreateDistWithIndex(out string dir)
    {
        dir = TempDir.Create();
        File.WriteAllText(Path.Combine(dir, "index.html"), "<html>SPA-INDEX</html>");
        return dir;
    }

    // ---------------- AC-4 ----------------

    private static readonly Dictionary<string, Func<Telegram.Bot.Types.Update>> IgnoredUpdates = new()
    {
        ["group message"] = () => TestUpdates.Group("/start"),
        ["supergroup message"] = () => TestUpdates.Group("/ping hi", type: "supergroup"),
        ["channel message"] = () => TestUpdates.Channel("/start"),
        ["channel_post"] = () => TestUpdates.ChannelPost("/start"),
        ["edited_message"] = () => TestUpdates.Edited("/ping hi"),
        ["message without from"] = () => TestUpdates.WithoutFrom("/start"),
        ["inline_query"] = () => TestUpdates.InlineQuery(),
        ["callback in group"] = () => TestUpdates.Callback("demo:act:1", chatType: "group", chatId: -100500),
        ["callback in supergroup"] = () => TestUpdates.Callback("demo:act:1", chatType: "supergroup", chatId: -100500),
        ["callback in channel"] = () => TestUpdates.Callback("demo:act:1", chatType: "channel", chatId: -100500),
        ["inline-mode callback"] = () => TestUpdates.Callback("demo:act:1", withMessage: false),
    };

    public static TheoryData<string> IgnoredNames() => new(IgnoredUpdates.Keys);

    [Theory]
    [MemberData(nameof(IgnoredNames))]
    public async Task AC4_UnsupportedUpdate_Returns200AndNothingHappens(string name)
    {
        var update = IgnoredUpdates[name]();
        var registry = BotHarness.NewRegistry();
        await using var host = await BotHarness.StartAsync(registry);

        var response = await BotHarness.PostWebhookAsync(host.Client, update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.BotApi!.Sent());
        Assert.Empty(host.BotApi!.Answers());
        Assert.Equal(0, host.Services.GetRequiredService<BotRecorder>().TotalHandlerCalls);
        await registry.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default);
    }

    [Fact]
    public async Task AC4_PrivateMessageAfterIgnoredOnes_StillProcessed()
    {
        await using var host = await BotHarness.StartAsync();

        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Group("/start"));
        await BotHarness.PostWebhookAsync(host.Client, TestUpdates.Start());

        Assert.Single(host.BotApi!.Sent());
    }
}
