using Bot.Platform.Auth;
using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Users;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Tests;

// TEST-3: AC-5..AC-11 (+ AC-22 order) directly through IUpdateDispatcher
public class BotDispatcherTests
{
    private static Task<DemoHost> StartAsync(
        string? publicUrl = TestBot.PublicUrl,
        IUserRegistry? registry = null,
        Action<WebApplicationBuilder>? addModules = null,
        bool includeDemoModule = true)
    {
        var settings = new Dictionary<string, string?>();
        if (publicUrl is not null) settings["Bot:PublicUrl"] = publicUrl;
        return BotHarness.StartAsync(registry ?? BotHarness.NewRegistry(), settings, addModules: addModules,
            includeDemoModule: includeDemoModule);
    }

    private static IReadOnlyList<BotApiMock.SentMessage> Sent(DemoHost h) => h.BotApi!.Sent();

    private static BotRecorder Recorder(DemoHost h) => h.Services.GetRequiredService<BotRecorder>();

    // ---------------- AC-5 ----------------

    [Theory]
    [InlineData("/start")]
    [InlineData("/START")]
    [InlineData("/start@AnyBot")]
    [InlineData("/Start@some_other_bot")]
    public async Task AC5_Start_RepliesWithWelcomeAndWebAppButton(string text)
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private(text, userId: 77));

        var message = Assert.Single(Sent(host));
        Assert.Equal(77, message.ChatId);
        Assert.Equal(TestBot.Welcome, message.Text);
        Assert.False(message.Html);
        var markup = Assert.IsType<InlineKeyboardMarkup>(message.ReplyMarkup);
        var button = Assert.Single(markup.InlineKeyboard.SelectMany(r => r));
        Assert.Equal("Открыть приложение", button.Text);
        Assert.NotNull(button.WebApp);
        Assert.Equal(new Uri(TestBot.PublicUrl), new Uri(button.WebApp.Url));
    }

    [Fact]
    public async Task AC5_PublicUrlWithPath_ButtonKeepsUrlAsGiven()
    {
        await using var host = await StartAsync("https://example.org/tma/");

        await host.DispatchAsync(TestUpdates.Start());

        var markup = Assert.IsType<InlineKeyboardMarkup>(Assert.Single(Sent(host)).ReplyMarkup);
        Assert.Equal(new Uri("https://example.org/tma/"), new Uri(markup.InlineKeyboard.Single().Single().WebApp!.Url));
    }

    [Fact]
    public async Task AC5_PublicUrlNotSet_SameTextWithoutButton()
    {
        await using var host = await StartAsync(publicUrl: null);

        await host.DispatchAsync(TestUpdates.Start());

        var message = Assert.Single(Sent(host));
        Assert.Equal(TestBot.Welcome, message.Text);
        Assert.Null(message.ReplyMarkup);
    }

    // ---------------- AC-6 ----------------

    [Theory]
    [InlineData("/help")]
    [InlineData("/help some args")]
    [InlineData("/HELP@AnyBot")]
    public async Task AC6_Help_ListsPlatformThenModulesInRegistrationOrder(string text)
    {
        await using var host = await StartAsync(addModules: b => b.AddModule<DemoBModule>());

        await host.DispatchAsync(TestUpdates.Private(text));

        var message = Assert.Single(Sent(host));
        Assert.Equal(TestBot.HelpBase + "\n/ping — Проверка связи\n/list — Список\n/zeta — Последняя", message.Text);
        Assert.Equal(
            "Команды:\n/start — открыть приложение\n/help — список команд\n/ping — Проверка связи\n/list — Список\n/zeta — Последняя",
            message.Text);
        Assert.Equal(0, Recorder(host).TotalHandlerCalls);
    }

    [Fact]
    public async Task AC6_NoModulesWithCommands_OnlyPlatformLines()
    {
        await using var host = await StartAsync(includeDemoModule: false);

        await host.DispatchAsync(TestUpdates.Command("help"));

        Assert.Equal("Команды:\n/start — открыть приложение\n/help — список команд", Assert.Single(Sent(host)).Text);
    }

    [Fact]
    public async Task AC6_ModuleWithoutMapBot_NoExtraLines()
    {
        await using var host = await StartAsync(includeDemoModule: false, addModules: b => b.AddModule<MinLengthNameModule>());

        await host.DispatchAsync(TestUpdates.Command("help"));

        Assert.Equal(TestBot.HelpBase, Assert.Single(Sent(host)).Text);
    }

    // ---------------- AC-7 ----------------

    [Theory]
    [InlineData("/nope")]
    [InlineData("/")]
    [InlineData("/ x")]
    [InlineData("/Привет")]
    [InlineData("/ping-x")]
    [InlineData("//")]
    public async Task AC7_UnknownOrMalformedCommand_RepliesUnknownCommand(string text)
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private(text));

        Assert.Equal(TestBot.UnknownCommand, Assert.Single(Sent(host)).Text);
        Assert.Equal(0, Recorder(host).TotalHandlerCalls);
    }

    [Fact]
    public async Task AC7_PlainText_RepliesOnlyCommands()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private("привет"));
        await host.DispatchAsync(TestUpdates.Private(" /ping"));   // leading space: not a command

        Assert.All(Sent(host), m => Assert.Equal(TestBot.NotACommand, m.Text));
        Assert.Equal(2, Sent(host).Count);
        Assert.Equal(0, Recorder(host).TotalHandlerCalls);
    }

    [Fact]
    public async Task AC7_Sticker_RepliesOnlyCommands()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Sticker());

        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
    }

    [Fact]
    public async Task AC7_PhotoWithCommandCaption_RepliesOnlyCommandsAndHandlerNotCalled()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.PhotoWithCaption("/ping"));

        Assert.Equal(TestBot.NotACommand, Assert.Single(Sent(host)).Text);
        Assert.Empty(Recorder(host).Commands);
    }

    // ---------------- AC-8 ----------------

    [Theory]
    [InlineData("/ping hello world", "hello world")]
    [InlineData("/ping@AnyBot hello world", "hello world")]
    [InlineData("/PING   hello world  ", "hello world")]
    [InlineData("/Ping\thello", "hello")]
    [InlineData("/ping a  b\nc", "a  b\nc")]
    [InlineData("/ping", "")]
    [InlineData("/ping   ", "")]
    [InlineData("/ping@AnyBot", "")]
    public async Task AC8_PingCommand_HandlerCalledOnceWithParsedContext(string text, string expectedArguments)
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private(text, userId: 4242));

        var ctx = Assert.Single(Recorder(host).Commands);
        Assert.Equal("ping", ctx.Command);
        Assert.Equal(expectedArguments, ctx.Arguments);
        Assert.Equal(4242, ctx.ChatId);
        Assert.Equal(4242, ctx.From.Id);
        Assert.True(ctx.From.AllowsWriteToPm);
        Assert.Equal(text, ctx.Message.Text);
        Assert.NotNull(ctx.Services);
        Assert.Same(host.Services.GetRequiredService<IMessageSender>(), ctx.Sender);
        Assert.Empty(Sent(host));   // platform does not reply on a routed command
    }

    [Fact]
    public async Task AC8_Command_ContextCarriesUserFields()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private("/list", userId: 5, firstName: "Анна", lastName: null, username: null,
            languageCode: null, isPremium: false));

        var ctx = Assert.Single(Recorder(host).Commands);
        Assert.Equal("list", ctx.Command);
        Assert.Equal(new TelegramUser(5, "Анна", null, null, null, false, true), ctx.From);
    }

    [Fact]
    public async Task AC8_HandlerReplyAsync_SendsToChat()
    {
        await using var host = await StartAsync();
        await host.DispatchAsync(TestUpdates.Command("ping", userId: 9));
        var ctx = Assert.Single(Recorder(host).Commands);

        var result = await ctx.ReplyAsync(new OutgoingMessage("pong"));

        Assert.Equal(SendStatus.Sent, result.Status);
        var message = Assert.Single(Sent(host));
        Assert.Equal((9L, "pong"), (message.ChatId, message.Text));
    }

    // ---------------- AC-9 ----------------

    [Theory]
    [InlineData("demo:act:42", "42")]
    [InlineData("demo:act:a:b", "a:b")]
    [InlineData("demo:act", "")]
    [InlineData("demo:act:", "")]
    public async Task AC9_Callback_HandlerCalledWithActionAndPayload(string data, string expectedPayload)
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Callback(data, userId: 31, queryId: "q-31"));

        var ctx = Assert.Single(Recorder(host).Callbacks);
        Assert.Equal("act", ctx.Action);
        Assert.Equal(expectedPayload, ctx.Payload);
        Assert.Equal(31, ctx.ChatId);
        Assert.Equal(31, ctx.From.Id);
        Assert.True(ctx.From.AllowsWriteToPm);
        Assert.Equal("q-31", ctx.Query.Id);
        Assert.Equal(data, ctx.Query.Data);
    }

    [Fact]
    public async Task AC9_HandlerDidNotAnswer_PlatformAnswersOnceWithoutText()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Callback("demo:act:1", queryId: "q1"));

        var answer = Assert.Single(host.BotApi!.Answers());
        Assert.Equal(new BotApiMock.AnsweredCallback("q1", null, false), answer);
    }

    [Fact]
    public async Task AC9_HandlerAnswers_AnsweredOnceWithItsParameters()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Callback("demo:answer:1", queryId: "q2"));

        var answer = Assert.Single(host.BotApi!.Answers());
        Assert.Equal(new BotApiMock.AnsweredCallback("q2", "Готово", true), answer);
    }

    [Fact]
    public async Task AC9_HandlerAnswersTwice_SecondCallIsNoOp()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Callback("demo:twice:1", queryId: "q3"));

        var answer = Assert.Single(host.BotApi!.Answers());
        Assert.Equal(new BotApiMock.AnsweredCallback("q3", "first", false), answer);
    }

    [Fact]
    public async Task AC9_HandlerThrows_StillAnsweredExactlyOnceWithoutText()
    {
        await using var host = await StartAsync();

        await Record.ExceptionAsync(() => host.DispatchAsync(TestUpdates.Callback("demo:boom:1", queryId: "q4")));

        var answer = Assert.Single(host.BotApi!.Answers());
        Assert.Equal(new BotApiMock.AnsweredCallback("q4", null, false), answer);
        Assert.Single(Recorder(host).Callbacks);
    }

    [Fact]
    public async Task AC9_TelegramFailsToAnswer_WarningLoggedAndNothingThrown()
    {
        await using var host = await StartAsync();
        host.BotApi!.AnswerCallbackQueryAsync(default!, default, default, default)
            .ThrowsAsyncForAnyArgs(TestBot.BadRequest("Bad Request: query is too old and response timeout expired"));

        var ex = await Record.ExceptionAsync(() => host.DispatchAsync(TestUpdates.Callback("demo:act:1", queryId: "q5")));

        Assert.Null(ex);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Single(host.BotApi.Answers());
    }

    [Fact]
    public async Task AC9_TelegramFailsToAnswerFromHandlerAnswerAsync_HandlerNotBrokenAndNothingThrown()
    {
        await using var host = await StartAsync();
        host.BotApi!.AnswerCallbackQueryAsync(default!, default, default, default)
            .ThrowsAsyncForAnyArgs(TestBot.BadRequest());

        var ex = await Record.ExceptionAsync(() => host.DispatchAsync(TestUpdates.Callback("demo:answer:1", queryId: "q6")));

        Assert.Null(ex);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Single(host.BotApi.Answers());
    }

    // ---------------- AC-10 ----------------

    [Theory]
    [InlineData("other:act:1")]      // unknown module
    [InlineData("demo:nope:1")]      // unknown action
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nocolon")]
    [InlineData(":act")]
    [InlineData("demo:")]
    public async Task AC10_StaleOrMalformedCallback_AnsweredOnceWithStaleTextAndNoHandlers(string? data)
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Callback(data, queryId: "stale"));

        var answer = Assert.Single(host.BotApi!.Answers());
        Assert.Equal(new BotApiMock.AnsweredCallback("stale", TestBot.StaleButton, false), answer);
        Assert.Equal(0, Recorder(host).TotalHandlerCalls);
        Assert.Empty(Sent(host));
    }

    // ---------------- AC-11 ----------------

    [Theory]
    [InlineData("demo_abc-1", "abc-1")]
    [InlineData("demo_a_b", "a_b")]
    [InlineData("demo_X", "X")]
    public async Task AC11_StartWithModulePayload_ModuleHandlerCalledAndNoGreeting(string argument, string expectedPayload)
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Start(argument, userId: 12));

        var ctx = Assert.Single(Recorder(host).Starts);
        Assert.Equal(expectedPayload, ctx.Payload);
        Assert.Equal(12, ctx.ChatId);
        Assert.Equal(12, ctx.From.Id);
        Assert.Empty(Sent(host));
    }

    [Fact]
    public async Task AC11_PayloadOfExactly64Chars_IsRouted()
    {
        await using var host = await StartAsync();
        var payload = new string('a', 59);   // "demo_" + 59 = 64

        await host.DispatchAsync(TestUpdates.Start("demo_" + payload));

        Assert.Equal(payload, Assert.Single(Recorder(host).Starts).Payload);
    }

    [Fact]
    public async Task AC11_StartWithBotSuffixAndPayload_IsRouted()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Private("/start@AnyBot demo_abc"));

        Assert.Equal("abc", Assert.Single(Recorder(host).Starts).Payload);
    }

    [Theory]
    [InlineData("other_x")]                                  // module not registered
    [InlineData("plain_x")]                                  // module registered, no MapStart
    [InlineData("demo")]
    [InlineData("demo_")]
    [InlineData("demo_a b")]
    [InlineData("Demo_x")]
    [InlineData("demo_x!")]
    [InlineData("demo_привет")]
    [InlineData("d_x")]                                      // module name too short
    public async Task AC11_InvalidOrUnroutablePayload_GreetingAndNoHandlers(string argument)
    {
        await using var host = await StartAsync(addModules: b => b.AddModule<NoMapBotModule>());

        await host.DispatchAsync(TestUpdates.Start(argument));

        Assert.Equal(TestBot.Welcome, Assert.Single(Sent(host)).Text);
        Assert.Equal(0, Recorder(host).TotalHandlerCalls);
    }

    [Fact]
    public async Task AC11_PayloadLongerThan64_GreetingAndNoHandlers()
    {
        await using var host = await StartAsync();

        await host.DispatchAsync(TestUpdates.Start("demo_" + new string('a', 60)));   // total 65

        Assert.Equal(TestBot.Welcome, Assert.Single(Sent(host)).Text);
        Assert.Empty(Recorder(host).Starts);
    }

    // ---------------- parallel updates ----------------

    [Fact]
    public async Task AC8_TwoUsersInParallel_SlowHandlerDoesNotBlockOtherAndContextsAreIsolated()
    {
        await using var host = await StartAsync();
        var rec = Recorder(host);

        var slow = host.DispatchAsync(TestUpdates.Command("list", "gate", userId: 1));
        await rec.GateEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.DispatchAsync(TestUpdates.Command("ping", "fast", userId: 2)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(slow.IsCompleted);
        rec.Gate.SetResult();
        await slow.WaitAsync(TimeSpan.FromSeconds(10));

        var byUser = rec.Commands.ToDictionary(c => c.From.Id);
        Assert.Equal(2, byUser.Count);
        Assert.Equal("list", byUser[1].Command);
        Assert.Equal(1, byUser[1].ChatId);
        Assert.Equal("ping", byUser[2].Command);
        Assert.Equal("fast", byUser[2].Arguments);
        Assert.Equal(2, byUser[2].ChatId);
        Assert.NotSame(byUser[1].Services, byUser[2].Services);
    }

    [Fact]
    public async Task AC8_ManyParallelUpdates_EachHandledOnceWithOwnData()
    {
        await using var host = await StartAsync();

        await Task.WhenAll(Enumerable.Range(1, 40).Select(i =>
            Task.Run(() => host.DispatchAsync(TestUpdates.Command("ping", "n" + i, userId: 100 + i)))));

        var commands = Recorder(host).Commands.ToList();
        Assert.Equal(40, commands.Count);
        Assert.All(commands, c => Assert.Equal("n" + (c.From.Id - 100), c.Arguments));
    }

    // ---------------- AC-22 (order and shape) ----------------

    [Fact]
    public async Task AC22_Command_UpsertCalledWithTelegramUserBeforeHandler()
    {
        var registry = BotHarness.NewRegistry();
        await using var host = await StartAsync(registry: registry);
        var rec = Recorder(host);
        var handlerCallsAtUpsert = -1;
        registry.When(r => r.UpsertAsync(Arg.Any<TelegramUser>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>()))
            .Do(_ => handlerCallsAtUpsert = rec.TotalHandlerCalls);

        await host.DispatchAsync(TestUpdates.Private("/ping x", userId: 8, firstName: "Иван", lastName: "Петров",
            username: "ivan", languageCode: "ru", isPremium: true));

        await registry.Received(1).UpsertAsync(
            new TelegramUser(8, "Иван", "Петров", "ivan", "ru", true, true), true, Arg.Any<CancellationToken>());
        Assert.Equal(0, handlerCallsAtUpsert);
        Assert.Single(rec.Commands);
    }

    [Fact]
    public async Task AC22_PlatformReply_UpsertCalledBeforeReply()
    {
        var registry = BotHarness.NewRegistry();
        await using var host = await StartAsync(registry: registry);

        await host.DispatchAsync(TestUpdates.Start());

        Received.InOrder(() =>
        {
            registry.UpsertAsync(Arg.Any<TelegramUser>(), true, Arg.Any<CancellationToken>());
            host.BotApi!.SendMessageAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<ReplyMarkup?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AC22_Callback_UpsertCalledBeforeHandlerAndAnswer()
    {
        var registry = BotHarness.NewRegistry();
        await using var host = await StartAsync(registry: registry);
        var rec = Recorder(host);
        var handlerCallsAtUpsert = -1;
        registry.When(r => r.UpsertAsync(Arg.Any<TelegramUser>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>()))
            .Do(_ => handlerCallsAtUpsert = rec.TotalHandlerCalls);

        await host.DispatchAsync(TestUpdates.Callback("demo:act:1", userId: 3));

        Assert.Equal(0, handlerCallsAtUpsert);
        Received.InOrder(() =>
        {
            registry.UpsertAsync(Arg.Is<TelegramUser>(u => u.Id == 3), true, Arg.Any<CancellationToken>());
            host.BotApi!.AnswerCallbackQueryAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        });
    }

    [Theory]
    [InlineData("/unknown")]
    [InlineData("just text")]
    public async Task AC22_UnknownCommandAndPlainText_StillUpserted(string text)
    {
        var registry = BotHarness.NewRegistry();
        await using var host = await StartAsync(registry: registry);

        await host.DispatchAsync(TestUpdates.Private(text, userId: 15));

        await registry.Received(1).UpsertAsync(Arg.Is<TelegramUser>(u => u.Id == 15), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC22_Sticker_Upserted()
    {
        var registry = BotHarness.NewRegistry();
        await using var host = await StartAsync(registry: registry);

        await host.DispatchAsync(TestUpdates.Sticker(userId: 16));

        await registry.Received(1).UpsertAsync(Arg.Is<TelegramUser>(u => u.Id == 16), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC22_StaleCallback_Upserted()
    {
        var registry = BotHarness.NewRegistry();
        await using var host = await StartAsync(registry: registry);

        await host.DispatchAsync(TestUpdates.Callback("zzz:1", userId: 17));

        await registry.Received(1).UpsertAsync(Arg.Is<TelegramUser>(u => u.Id == 17), true, Arg.Any<CancellationToken>());
    }
}
