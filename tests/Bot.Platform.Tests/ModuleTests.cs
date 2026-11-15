using System.Net;
using Bot.Platform.Modules;
using Bot.Platform.Tests.Helpers;

namespace Bot.Platform.Tests;

// TEST-2: AC-1..AC-4
public class ModuleTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(7L)]
    [InlineData(4503599627370495L)] // 2^52 - 1: largest id that fits into 52 bits
    public async Task AC1_ValidInitDataOfUser_ModulePingReturns200AndUserId(long userId)
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/ping", userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(userId.ToString(System.Globalization.CultureInfo.InvariantCulture), (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task AC1_ModuleAddServices_ServicesAvailableInDiAndInHandlers()
    {
        await using var host = await DemoHost.StartAsync();

        Assert.NotNull(host.Services.GetService<DemoMarker>());
        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/marker", 42);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("demo-marker", (await ApiCall.JsonAsync(response)).GetString());
    }

    [Fact]
    public async Task AC2_NoAuthorizationHeader_Returns401AuthMissingAndHandlerNotCalled()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsync(host.Client, "/api/demo/ping");

        await ApiCall.AssertProblemAsync(response, 401, "auth_missing");
        Assert.Equal(0, host.Services.GetRequiredService<PingCounter>().Count);
    }

    [Fact]
    public async Task AC2_ValidCallAfterRejected_HandlerCalledOnlyForAuthorizedRequest()
    {
        await using var host = await DemoHost.StartAsync();

        await ApiCall.GetAsync(host.Client, "/api/demo/ping");
        await ApiCall.GetAsUserAsync(host.Client, "/api/demo/ping", 42);

        Assert.Equal(1, host.Services.GetRequiredService<PingCounter>().Count);
    }

    // ---- AC-3: invalid / reserved / duplicate module names ----

    private static InvalidOperationException AddModuleRejected<TModule>() where TModule : class, IBotModule, new()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Bot:Token"] = TestInitData.BotToken;
        builder.AddPlatform();
        return Assert.Throws<InvalidOperationException>(() => builder.AddModule<TModule>());
    }

    private static void AddModuleAccepted<TModule>() where TModule : class, IBotModule, new()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Bot:Token"] = TestInitData.BotToken;
        builder.AddPlatform();
        builder.AddModule<TModule>();
    }

    [Fact]
    public void AC3_EmptyName_ThrowsInvalidOperationException() => AddModuleRejected<EmptyNameModule>();

    [Fact]
    public void AC3_UppercaseName_ThrowsAndMessageHasName() =>
        Assert.Contains("Demo", AddModuleRejected<UppercaseNameModule>().Message);

    [Fact]
    public void AC3_NameWithUnderscore_ThrowsAndMessageHasName() =>
        Assert.Contains("demo_x", AddModuleRejected<UnderscoreNameModule>().Message);

    [Fact]
    public void AC3_NameStartsWithDigit_ThrowsAndMessageHasName() =>
        Assert.Contains("1demo", AddModuleRejected<DigitFirstNameModule>().Message);

    [Fact]
    public void AC3_SingleCharName_Throws() => AddModuleRejected<OneCharNameModule>();

    [Fact]
    public void AC3_Name33Chars_ThrowsAndMessageHasName() =>
        Assert.Contains("abcdefghijklmnopqrstuvwxyzabcdefg", AddModuleRejected<TooLongNameModule>().Message);

    [Fact]
    public void AC3_Name32Chars_IsAccepted() => AddModuleAccepted<MaxLengthNameModule>();

    [Fact]
    public void AC3_Name2Chars_IsAccepted() => AddModuleAccepted<MinLengthNameModule>();

    [Fact]
    public void AC3_ReservedPlatform_ThrowsAndMessageHasName() =>
        Assert.Contains("platform", AddModuleRejected<ReservedPlatformModule>().Message);

    [Fact]
    public void AC3_ReservedBot_ThrowsAndMessageHasName() =>
        Assert.Contains("bot", AddModuleRejected<ReservedBotModule>().Message);

    [Fact]
    public void AC3_ReservedHealth_ThrowsAndMessageHasName() =>
        Assert.Contains("health", AddModuleRejected<ReservedHealthModule>().Message);

    [Fact]
    public void AC3_ReservedAssets_ThrowsAndMessageHasName() =>
        Assert.Contains("assets", AddModuleRejected<ReservedAssetsModule>().Message);

    [Fact]
    public void AC3_DuplicateName_ThrowsOnSecondRegistration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Bot:Token"] = TestInitData.BotToken;
        builder.AddPlatform();
        builder.AddModule<DemoModule>();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddModule<DemoModule>());

        Assert.Contains("demo", ex.Message);
    }

    [Fact]
    public async Task AC4_HostWithoutModules_HealthReturns200OkWithoutAuth()
    {
        await using var host = await DemoHost.StartAsync(includeDemoModule: false);

        var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiCall.JsonAsync(response);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC4_HealthWithGarbageAuthorizationHeader_StillReturns200()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var response = await ApiCall.GetAsync(client, "/health", "tma garbage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
