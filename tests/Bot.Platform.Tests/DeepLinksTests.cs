using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Bot.Platform.Tests;

// Spec 003, TEST-4: AC-13..AC-15 (IDeepLinks, Bot:Username / MiniAppShortName / ReplyTimeout)
public class DeepLinksTests
{
    private static async Task<(DemoHost Host, IDeepLinks Links)> StartAsync(
        string? username = "MovieNightBot", string? shortName = "app", string? publicUrl = null)
    {
        var settings = new Dictionary<string, string?>();
        if (username is not null) settings["Bot:Username"] = username;
        if (shortName is not null) settings["Bot:MiniAppShortName"] = shortName;
        if (publicUrl is not null) settings["Bot:PublicUrl"] = publicUrl;
        var host = await BotHarness.StartAsync(settings: settings);
        return (host, host.Services.GetRequiredService<IDeepLinks>());
    }

    // ---------------- AC-13 ----------------

    [Fact]
    public async Task AC13_UsernameAndShortName_BuildsDirectLinks()
    {
        var (host, links) = await StartAsync();
        await using var _ = host;

        Assert.Equal("https://t.me/MovieNightBot/app?startapp=movienight_join_AB12cd34EF", links.MiniApp("movienight", "join_AB12cd34EF"));
        Assert.Equal("https://t.me/MovieNightBot?start=movienight_join_AB12cd34EF", links.BotStart("movienight", "join_AB12cd34EF"));
    }

    [Fact]
    public async Task AC13_NoShortName_MiniAppLinkLeadsToMainMiniApp_BotStartUnchanged()
    {
        var (host, links) = await StartAsync(shortName: null);
        await using var _ = host;

        Assert.Equal("https://t.me/MovieNightBot?startapp=movienight_join_AB12cd34EF", links.MiniApp("movienight", "join_AB12cd34EF"));
        Assert.Equal("https://t.me/MovieNightBot?start=movienight_join_AB12cd34EF", links.BotStart("movienight", "join_AB12cd34EF"));
    }

    [Fact]
    public async Task AC13_NoUsername_BothNull_EvenWithShortName()
    {
        var (host, links) = await StartAsync(username: null);
        await using var _ = host;

        Assert.Null(links.MiniApp("movienight", "join_x"));
        Assert.Null(links.BotStart("movienight", "join_x"));
    }

    [Fact]
    public async Task AC13_NoUsername_InvalidArguments_ThrowInsteadOfNull()
    {
        var (host, links) = await StartAsync(username: null);
        await using var _ = host;

        Assert.ThrowsAny<ArgumentException>(() => links.MiniApp("A", "ok"));
        Assert.ThrowsAny<ArgumentException>(() => links.BotStart("movienight", "a b"));
        Assert.ThrowsAny<ArgumentException>(() => links.MiniApp("movienight", ""));
        Assert.ThrowsAny<ArgumentException>(() => links.BotStart("movienight", new string('A', 64)));
        Assert.Throws<ArgumentNullException>(() => links.MiniApp(null!, "ok"));
        Assert.Throws<ArgumentNullException>(() => links.BotStart("movienight", null!));
    }

    [Fact]
    public async Task AC13_IsSingleton()
    {
        var (host, links) = await StartAsync();
        await using var _ = host;

        await using var scope = host.Services.CreateAsyncScope();
        Assert.Same(links, scope.ServiceProvider.GetRequiredService<IDeepLinks>());
    }

    // ---------------- AC-14 ----------------

    public static TheoryData<string> BadModules() => new()
    {
        "A", "a", "1ab", "ab-c", "Ab", "a_b", "", "abcdefghijklmnopqrstuvwxyzabcdefg", "модуль",
    };

    [Theory]
    [MemberData(nameof(BadModules))]
    public async Task AC14_InvalidModule_ThrowsArgumentException(string module)
    {
        var (host, links) = await StartAsync();
        await using var _ = host;

        Assert.ThrowsAny<ArgumentException>(() => links.MiniApp(module, "ok"));
        Assert.ThrowsAny<ArgumentException>(() => links.BotStart(module, "ok"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("a!b")]
    [InlineData("пейлоад")]
    [InlineData("a\nb")]
    [InlineData("a&b=1")]
    public async Task AC14_InvalidPayload_ThrowsArgumentException(string payload)
    {
        var (host, links) = await StartAsync();
        await using var _ = host;

        Assert.ThrowsAny<ArgumentException>(() => links.MiniApp("movienight", payload));
        Assert.ThrowsAny<ArgumentException>(() => links.BotStart("movienight", payload));
    }

    [Fact]
    public async Task AC14_NullArguments_Throw()
    {
        var (host, links) = await StartAsync();
        await using var _ = host;

        Assert.ThrowsAny<ArgumentException>(() => links.MiniApp(null!, "ok"));
        Assert.ThrowsAny<ArgumentException>(() => links.BotStart("movienight", null!));
    }

    [Fact]
    public async Task AC14_CombinedLength_64Ok_65Throws()
    {
        var (host, links) = await StartAsync();
        await using var _ = host;
        var ok = new string('A', 64 - "movienight_".Length);
        var tooLong = ok + "A";

        Assert.EndsWith("startapp=movienight_" + ok, links.MiniApp("movienight", ok));
        Assert.EndsWith("start=movienight_" + ok, links.BotStart("movienight", ok));
        Assert.ThrowsAny<ArgumentException>(() => links.MiniApp("movienight", tooLong));
        Assert.ThrowsAny<ArgumentException>(() => links.BotStart("movienight", tooLong));
    }

    [Fact]
    public async Task AC14_PayloadAllowedAlphabet_Accepted()
    {
        var (host, links) = await StartAsync();
        await using var _ = host;

        Assert.EndsWith("movienight_aZ09_-", links.BotStart("movienight", "aZ09_-"));
    }

    [Fact]
    public async Task AC14_WebAppUrl_EqualsPublicUrl_OrNull()
    {
        var (h1, l1) = await StartAsync(publicUrl: "https://example.org/tma/");
        await using var _1 = h1;
        var (h2, l2) = await StartAsync();
        await using var _2 = h2;

        Assert.Equal(new Uri("https://example.org/tma/"), l1.WebAppUrl);
        Assert.Null(l2.WebAppUrl);
    }

    // ---------------- AC-15 ----------------

    private static async Task<Exception> StartFailureAsync(string key, string value)
    {
        var settings = new Dictionary<string, string?> { [key] = value };
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var host = await BotHarness.StartAsync(settings: settings);
        });
        Assert.NotNull(ex);
        return ex;
    }

    private static void AssertNamesKeyNotValue(Exception ex, string key, string value)
    {
        Assert.IsType<OptionsValidationException>(ex);
        Assert.Contains(key, ex.Message);
        Assert.DoesNotContain(value, ex.Message);
    }

    [Theory]
    [InlineData("@MovieNightBot")]
    [InlineData("abcd")]
    [InlineData("1abcde")]
    [InlineData("_abcde")]
    [InlineData("zz-bad-name")]
    [InlineData("zz bad")]
    [InlineData("zzбот_имя")]
    [InlineData("zzabcdefghijklmnopqrstuvwxyzabcdefghij")]
    [InlineData("t.me/zzBot")]
    public async Task AC15_InvalidUsername_StartupFailsNamingKeyNotValue(string value)
    {
        var ex = await StartFailureAsync("Bot:Username", value);

        AssertNamesKeyNotValue(ex, "Bot:Username", value);
    }

    [Theory]
    [InlineData("abcde")]
    [InlineData("A_bcd")]
    [InlineData("Abcdefghijklmnopqrstuvwxyz012345")]   // 32
    public async Task AC15_ValidUsername_Starts(string value)
    {
        await using var host = await BotHarness.StartAsync(settings: new() { ["Bot:Username"] = value });
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("zz-x")]
    [InlineData("zz x")]
    [InlineData("zzabcdefghijklmnopqrstuvwxyz0123")]   // 32
    [InlineData("имя")]
    public async Task AC15_InvalidShortName_StartupFailsNamingKeyNotValue(string value)
    {
        var ex = await StartFailureAsync("Bot:MiniAppShortName", value);

        AssertNamesKeyNotValue(ex, "Bot:MiniAppShortName", value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("A_9")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123")]   // 30
    public async Task AC15_ValidShortName_Starts(string value)
    {
        await using var host = await BotHarness.StartAsync(settings: new() { ["Bot:MiniAppShortName"] = value });
    }

    [Theory]
    [InlineData("00:00:59")]
    [InlineData("00:00:07")]
    [InlineData("-00:01:00")]
    [InlineData("1.00:00:01")]
    [InlineData("2.00:00:00")]
    public async Task AC15_ReplyTimeoutOutOfRange_StartupFailsNamingKey(string value)
    {
        var ex = await StartFailureAsync("Bot:ReplyTimeout", value);

        AssertNamesKeyNotValue(ex, "Bot:ReplyTimeout", value);
    }

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("00:10:00")]
    [InlineData("1.00:00:00")]
    public async Task AC15_ReplyTimeoutInRange_Starts(string value)
    {
        await using var host = await BotHarness.StartAsync(settings: new() { ["Bot:ReplyTimeout"] = value });
    }

    [Fact]
    public async Task AC15_NothingSet_StartsWithDefaults()
    {
        await using var host = await BotHarness.StartAsync();

        var links = host.Services.GetRequiredService<IDeepLinks>();
        Assert.Null(links.MiniApp("movienight", "x"));
    }
}
