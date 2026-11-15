using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Bot.Platform.Tests;

// TEST-7: AC-15 (registry), AC-22 on a real database
public class BotUserRegistryTests
{
    private static async Task<PlatformUser?> FindAsync(DemoHost host, long id)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserDirectory>().FindAsync(id, CancellationToken.None);
    }

    [Fact]
    public async Task AC22_PrivateMessage_CreatesRecordWithNamesAndCanReceive()
    {
        await using var host = await BotHarness.StartAsync();

        await host.DispatchAsync(TestUpdates.Private("/start", 501, "Иван", "Петров", "ivan", "ru"));

        var user = await FindAsync(host, 501);
        Assert.NotNull(user);
        Assert.Equal(("Иван", "Петров", "ivan", "ru", true), (user.FirstName, user.LastName, user.Username, user.LanguageCode, user.CanReceiveMessages));
        Assert.Equal(host.Clock.UtcNow, user.FirstSeenAt);
    }

    [Theory]
    [InlineData("sticker")]
    [InlineData("unknown")]
    [InlineData("text")]
    public async Task AC22_AnyPrivateMessageKind_CreatesRecord(string kind)
    {
        await using var host = await BotHarness.StartAsync();
        var update = kind switch
        {
            "sticker" => TestUpdates.Sticker(502),
            "unknown" => TestUpdates.Private("/nope", 502),
            _ => TestUpdates.Private("привет", 502),
        };

        await host.DispatchAsync(update);

        Assert.NotNull(await FindAsync(host, 502));
    }

    [Fact]
    public async Task AC22_Callback_CreatesRecord()
    {
        await using var host = await BotHarness.StartAsync();

        await host.DispatchAsync(TestUpdates.Callback("demo:act:1", 503));

        Assert.True((await FindAsync(host, 503))!.CanReceiveMessages);
    }

    [Fact]
    public async Task AC22_NamesChange_RecordUpdatedFirstSeenAtUnchanged()
    {
        await using var host = await BotHarness.StartAsync();
        await host.DispatchAsync(TestUpdates.Private("/start", 504, "Старое", "Имя", "old"));
        var first = await FindAsync(host, 504);
        host.Clock.Advance(TimeSpan.FromHours(3));

        await host.DispatchAsync(TestUpdates.Private("/start", 504, "Новое", null, "new"));

        var second = await FindAsync(host, 504);
        Assert.Equal(("Новое", null, "new"), (second!.FirstName, second.LastName, second.Username));
        Assert.Equal(first!.FirstSeenAt, second.FirstSeenAt);
        Assert.True(second.LastSeenAt > first.LastSeenAt);
    }

    [Fact]
    public async Task AC15_AC22_BlockedThenMessage_FlagFalseThenTrue()
    {
        await using var host = await BotHarness.StartAsync();
        await host.DispatchAsync(TestUpdates.Private("/start", 505));
        var firstSeen = (await FindAsync(host, 505))!.FirstSeenAt;
        host.BotApi!.SendMessageAsync(default, default!, default, default, default).ThrowsAsyncForAnyArgs(TestBot.Forbidden());
        var sender = host.Services.GetRequiredService<IMessageSender>();

        var result = await sender.SendAsync(505, new OutgoingMessage("hi"), CancellationToken.None);

        Assert.Equal(SendStatus.Blocked, result.Status);
        Assert.False((await FindAsync(host, 505))!.CanReceiveMessages);

        host.Clock.Advance(TimeSpan.FromDays(1));
        host.BotApi.SendMessageAsync(default, default!, default, default, default).ReturnsForAnyArgs(Task.FromResult(1));
        await host.DispatchAsync(TestUpdates.Private("/start", 505));

        var user = (await FindAsync(host, 505))!;
        Assert.True(user.CanReceiveMessages);
        Assert.Equal(firstSeen, user.FirstSeenAt);
    }

    [Fact]
    public async Task AC15_BlockedForUnknownUser_NoRecordAndNoException()
    {
        await using var host = await BotHarness.StartAsync();
        host.BotApi!.SendMessageAsync(default, default!, default, default, default).ThrowsAsyncForAnyArgs(TestBot.Forbidden());

        var result = await host.Services.GetRequiredService<IMessageSender>()
            .SendAsync(999, new OutgoingMessage("hi"), CancellationToken.None);

        Assert.Equal(SendStatus.Blocked, result.Status);
        Assert.Null(await FindAsync(host, 999));
    }

    [Fact]
    public async Task AC22_FilteredUpdates_DoNotCreateRecords()
    {
        await using var host = await BotHarness.StartAsync();

        await host.DispatchAsync(TestUpdates.Group("/start", 601));
        await host.DispatchAsync(TestUpdates.Edited("x", 602));
        await host.DispatchAsync(TestUpdates.Callback("demo:act:1", 603, chatType: "supergroup", chatId: -100));
        await host.DispatchAsync(TestUpdates.Callback("demo:act:1", 604, withMessage: false));
        await host.DispatchAsync(TestUpdates.InlineQuery(605));

        foreach (var id in new long[] { 601, 602, 603, 604, 605 })
        {
            Assert.Null(await FindAsync(host, id));
        }
    }

    [Fact]
    public async Task AC22_ConcurrentUpdatesOfSameNewUser_ProduceSingleRecord()
    {
        await using var host = await BotHarness.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => host.DispatchAsync(TestUpdates.Private("привет", 700)))));

        Assert.NotNull(await FindAsync(host, 700));
    }
}
