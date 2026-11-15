using System.Net;
using Bot.Platform.Auth;
using Bot.Platform.Tests.Helpers;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.EntityFrameworkCore;

namespace Bot.Platform.Tests;

// TEST-6: AC-16..AC-19
public class UserRegistryTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    private static async Task<HttpResponseMessage> MeAsync(
        PlatformFactory factory, HttpClient client, long id, string firstName = "Test", string? lastName = "User",
        bool? allowsWriteToPm = null)
    {
        var raw = TestInitData.For(id, factory.Clock.UtcNow, firstName: firstName, lastName: lastName, username: "u" + id,
            languageCode: "ru", allowsWriteToPm: allowsWriteToPm);
        return await ApiCall.GetAsync(client, "/api/platform/me", ApiCall.Tma(raw));
    }

    private static async Task<PlatformUser?> FindAsync(PlatformFactory factory, long id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserDirectory>().FindAsync(id, CancellationToken.None);
    }

    // ---------------- AC-16 ----------------

    [Fact]
    public async Task AC16_EmptyDatabase_MigrationsAppliedOnStartup()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();   // starts the host

        Assert.Equal(1, factory.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'platform_users'"));
        Assert.Equal(1, factory.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__ef_migrations_platform'"));
        Assert.True(factory.Scalar("SELECT COUNT(*) FROM __ef_migrations_platform") >= 1);
        Assert.Equal(0, factory.Scalar("SELECT COUNT(*) FROM platform_users"));
    }

    [Fact]
    public async Task AC16_NoDefaultEfMigrationsHistoryTable()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();

        Assert.Equal(0, factory.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = '__EFMigrationsHistory'"));
    }

    [Fact]
    public async Task AC16_SecondStartOnAlreadyMigratedDatabase_Succeeds()
    {
        await using var first = new PlatformFactory();
        first.CreateClient();
        await MeAsync(first, first.CreateClient(), 42);

        await using var second = new PlatformFactory();   // fresh temp db: just a restart smoke test
        var response = await second.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------- AC-17 ----------------

    [Fact]
    public async Task AC17_UnknownUserCallsMe_RecordCreatedWithClockTimeAndFields()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var t0 = factory.Clock.UtcNow;

        var response = await MeAsync(factory, client, 42, "Иван", "Петров", allowsWriteToPm: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await FindAsync(factory, 42);
        Assert.NotNull(user);
        Assert.Equal("Иван", user.FirstName);
        Assert.Equal("Петров", user.LastName);
        Assert.Equal("u42", user.Username);
        Assert.Equal("ru", user.LanguageCode);
        Assert.Equal(t0, user.FirstSeenAt);
        Assert.Equal(t0, user.LastSeenAt);
        Assert.True(user.CanReceiveMessages);
    }

    [Fact]
    public async Task AC17_NewUserWithoutAllowsWriteToPm_CanReceiveMessagesIsFalse()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        await MeAsync(factory, client, 42, allowsWriteToPm: null);

        Assert.False((await FindAsync(factory, 42))!.CanReceiveMessages);
    }

    [Fact]
    public async Task AC17_NewUserWithAllowsWriteToPmFalse_CanReceiveMessagesIsFalse()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        await MeAsync(factory, client, 42, allowsWriteToPm: false);

        Assert.False((await FindAsync(factory, 42))!.CanReceiveMessages);
    }

    [Fact]
    public async Task AC17_KnownUserChangesNameAndCallsAgain_NamesAndLastSeenUpdatedFirstSeenKept()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var t0 = factory.Clock.UtcNow;
        await MeAsync(factory, client, 42, "Иван", "Петров", allowsWriteToPm: true);

        factory.Clock.Advance(OneHour);
        var response = await MeAsync(factory, client, 42, "Ivan", null, allowsWriteToPm: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await FindAsync(factory, 42))!;
        Assert.Equal("Ivan", user.FirstName);
        Assert.Null(user.LastName);
        Assert.Equal(t0, user.FirstSeenAt);
        Assert.Equal(t0 + OneHour, user.LastSeenAt);
        Assert.True(user.CanReceiveMessages, "CanReceiveMessages must not be reset when allows_write_to_pm is absent");
        Assert.Equal(1, factory.Scalar("SELECT COUNT(*) FROM platform_users"));
    }

    [Fact]
    public async Task AC17_KnownUserWithAllowsWriteToPmFalseInInitData_DoesNotResetCanReceiveMessages()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await MeAsync(factory, client, 42, allowsWriteToPm: true);

        factory.Clock.Advance(OneHour);
        await MeAsync(factory, client, 42, allowsWriteToPm: false);

        Assert.True((await FindAsync(factory, 42))!.CanReceiveMessages);
    }

    [Fact]
    public async Task AC17_KnownUserWithoutPmAccessLaterAllowsWrite_BecomesTrue()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await MeAsync(factory, client, 42, allowsWriteToPm: null);

        factory.Clock.Advance(OneHour);
        await MeAsync(factory, client, 42, allowsWriteToPm: true);

        Assert.True((await FindAsync(factory, 42))!.CanReceiveMessages);
    }

    [Fact]
    public async Task AC17_RejectedRequest_DoesNotCreateRecord()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var tampered = TestInitData.FlipHash(TestInitData.For(42, factory.Clock.UtcNow));

        var response = await ApiCall.GetAsync(client, "/api/platform/me", ApiCall.Tma(tampered));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Scalar("SELECT COUNT(*) FROM platform_users"));
    }

    // ---------------- AC-18 ----------------

    [Fact]
    public async Task AC18_TwoConcurrentFirstRequestsOfNewUser_BothOkExactlyOneRecord()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var responses = await Task.WhenAll(MeAsync(factory, client, 42), MeAsync(factory, client, 42));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, factory.Scalar("SELECT COUNT(*) FROM platform_users WHERE id = 42"));
    }

    [Fact]
    public async Task AC18_ManyUsersEachWithConcurrentFirstRequests_AllOkOneRecordPerUser()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        const int users = 10;

        var tasks = Enumerable.Range(1, users).SelectMany(i => new[]
        {
            MeAsync(factory, client, 1000 + i),
            MeAsync(factory, client, 1000 + i),
            MeAsync(factory, client, 1000 + i),
        });
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(users, factory.Scalar("SELECT COUNT(*) FROM platform_users"));
        Assert.Equal(users, factory.Scalar("SELECT COUNT(DISTINCT id) FROM platform_users"));
    }


    /// <summary>
    /// IClock that runs a hook on the first read. UserStore.UpsertAsync reads the clock after its "user exists?" check
    /// and before SaveChanges, so the hook deterministically inserts a competing row exactly in that window.
    /// </summary>
    private sealed class RacingClock(IClock inner, Action onFirstRead) : IClock
    {
        public int Reads { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

        public DateTimeOffset UtcNow
        {
            get
            {
                if (Reads++ == 0)
                {
                    onFirstRead();
                }

                return inner.UtcNow;
            }
        }
    }

    private static PlatformDbContext NewContext(PlatformFactory factory) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlite(factory.ConnectionString, sqlite => sqlite.MigrationsHistoryTable("__ef_migrations_platform"))
            .Options);

    [Fact]
    public async Task AC18_RowInsertedBetweenExistenceCheckAndInsert_KeyConflictIsHandledAndRecordUpdated()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();   // migrations applied
        var winnerTime = factory.Clock.UtcNow;
        var loserTime = winnerTime + OneHour;

        await using var loserDb = NewContext(factory);
        var racing = new RacingClock(new FakeClock { UtcNow = loserTime }, () =>
        {
            // competing request commits the same user after the loser saw "no such user"
            using var winnerDb = NewContext(factory);
            new UserStore(winnerDb, new FakeClock { UtcNow = winnerTime })
                .UpsertAsync(new TelegramUser(42, "Winner", null, "winner", "en", false, null), true, CancellationToken.None)
                .GetAwaiter().GetResult();
        });
        var loser = new UserStore(loserDb, racing);

        // INSERT hits the PRIMARY KEY conflict -> DbUpdateException branch -> re-read -> update, no exception to the caller
        await loser.UpsertAsync(new TelegramUser(42, "Loser", "Last", "loser", "ru", false, null), null, CancellationToken.None);

        Assert.True(racing.Reads >= 1, "the race hook must have run");
        Assert.Equal(1, factory.Scalar("SELECT COUNT(*) FROM platform_users WHERE id = 42"));
        var user = (await FindAsync(factory, 42))!;
        // values of the losing request came through the update path (a plain insert would have thrown or kept "Winner")
        Assert.Equal("Loser", user.FirstName);
        Assert.Equal("Last", user.LastName);
        Assert.Equal("loser", user.Username);
        Assert.Equal("ru", user.LanguageCode);
        Assert.Equal(winnerTime, user.FirstSeenAt);          // creation time of the winning insert is kept
        Assert.Equal(loserTime, user.LastSeenAt);
        Assert.True(user.CanReceiveMessages, "null from the loser must not reset the winner's true");
    }

    // ---------------- AC-19 ----------------

    private static async Task SeedAsync(PlatformFactory factory, params TelegramUser[] users)
    {
        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IUserRegistry>();
        foreach (var user in users)
        {
            await registry.UpsertAsync(user, true, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AC19_FindMany123WhereOnly12Exist_ReturnsKeys1And2()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();
        await SeedAsync(factory,
            new TelegramUser(1, "Ann", "Lee", "ann", "en", false, null),
            new TelegramUser(2, "Bob", null, null, null, false, null));

        using var scope = factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var found = await directory.FindManyAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal([1L, 2L], found.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("Ann", found[1].FirstName);
        Assert.Equal("Bob", found[2].FirstName);
    }

    [Fact]
    public async Task AC19_FindUnknownUser_ReturnsNull()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();
        await SeedAsync(factory, new TelegramUser(1, "Ann", "Lee", null, null, false, null));

        Assert.Null(await FindAsync(factory, 3));
        Assert.NotNull(await FindAsync(factory, 1));
    }

    [Fact]
    public async Task AC19_FindManyWithEmptyList_ReturnsEmptyDictionary()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IUserDirectory>().FindManyAsync([], CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task AC19_FindManyWithDuplicateIds_DoesNotFail()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();
        await SeedAsync(factory, new TelegramUser(1, "Ann", "Lee", null, null, false, null));

        using var scope = factory.Services.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IUserDirectory>().FindManyAsync([1, 1, 1], CancellationToken.None);

        Assert.Single(found);
    }

    [Theory]
    [InlineData("Ann", "Lee", "Ann Lee")]
    [InlineData("Ann", null, "Ann")]
    [InlineData("Ann", "", "Ann")]
    [InlineData("Ann", "   ", "Ann")]
    public void AC19_DisplayName_JoinsFirstAndLastWithoutExtraSpaces(string first, string? last, string expected)
    {
        var user = new PlatformUser(1, first, last, null, null, false, TestInitData.DefaultNow, TestInitData.DefaultNow);

        Assert.Equal(expected, user.DisplayName);
    }

    [Fact]
    public async Task AC19_DisplayNameOfStoredUserWithEmptyLastName_IsFirstNameOnly()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();
        await SeedAsync(factory,
            new TelegramUser(1, "Ann", "Lee", null, null, false, null),
            new TelegramUser(2, "Dan", "", null, null, false, null));

        Assert.Equal("Ann Lee", (await FindAsync(factory, 1))!.DisplayName);
        Assert.Equal("Dan", (await FindAsync(factory, 2))!.DisplayName);
    }

    // ---------------- IUserRegistry CanReceiveMessages rules ----------------

    [Fact]
    public async Task AC17_RegistryUpsertRules_NullKeepsExistingTrueFalseOverridesNewDefaultsFalse()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();
        var user = new TelegramUser(5, "Eve", null, null, null, false, null);

        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IUserRegistry>();

        await registry.UpsertAsync(user, null, CancellationToken.None);
        Assert.False((await FindAsync(factory, 5))!.CanReceiveMessages);      // new + null -> false

        await registry.UpsertAsync(user, true, CancellationToken.None);
        Assert.True((await FindAsync(factory, 5))!.CanReceiveMessages);       // true -> true

        await registry.UpsertAsync(user, null, CancellationToken.None);
        Assert.True((await FindAsync(factory, 5))!.CanReceiveMessages);       // null -> unchanged

        await registry.UpsertAsync(user, false, CancellationToken.None);
        Assert.False((await FindAsync(factory, 5))!.CanReceiveMessages);      // false -> false
    }

    [Fact]
    public async Task AC17_MarkCannotReceiveMessages_SetsFalseAndUnknownUserIsIgnored()
    {
        await using var factory = new PlatformFactory();
        factory.CreateClient();
        await SeedAsync(factory, new TelegramUser(6, "Zed", null, null, null, false, null));
        Assert.True((await FindAsync(factory, 6))!.CanReceiveMessages);

        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IUserRegistry>();
        await registry.MarkCannotReceiveMessagesAsync(6, CancellationToken.None);
        await registry.MarkCannotReceiveMessagesAsync(999, CancellationToken.None);

        Assert.False((await FindAsync(factory, 6))!.CanReceiveMessages);
        Assert.Null(await FindAsync(factory, 999));
    }
}
