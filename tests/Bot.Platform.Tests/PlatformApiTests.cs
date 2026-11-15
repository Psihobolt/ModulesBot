using System.Net;
using System.Text.Json;
using Bot.Platform.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Bot.Platform.Tests;

// TEST-4: AC-9..AC-13 (+ ApiErrors helper contract)
public class PlatformApiTests
{
    // ---------------- AC-9 ----------------

    [Fact]
    public async Task AC9_ValidInitDataWithAllUserFields_MeReturnsMeDto()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var raw = TestInitData.For(42, factory.Clock.UtcNow, firstName: "Иван", lastName: "Петров", username: "ivan", languageCode: "ru");

        var response = await ApiCall.GetAsync(client, "/api/platform/me", ApiCall.Tma(raw));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiCall.JsonAsync(response);
        Assert.Equal(42, body.GetProperty("id").GetInt64());
        Assert.Equal("Иван", body.GetProperty("firstName").GetString());
        Assert.Equal("Петров", body.GetProperty("lastName").GetString());
        Assert.Equal("ivan", body.GetProperty("username").GetString());
        Assert.Equal("ru", body.GetProperty("languageCode").GetString());
    }

    [Fact]
    public async Task AC9_OptionalUserFieldsAbsent_MeReturnsNulls()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var raw = TestInitData.For(42, factory.Clock.UtcNow, firstName: "Solo", lastName: null, username: null, languageCode: null);

        var response = await ApiCall.GetAsync(client, "/api/platform/me", ApiCall.Tma(raw));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiCall.JsonAsync(response);
        Assert.Equal("Solo", body.GetProperty("firstName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastName").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("username").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("languageCode").ValueKind);
    }

    [Fact]
    public async Task AC9_MeWithoutAuthorization_Returns401AuthMissing()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var response = await ApiCall.GetAsync(client, "/api/platform/me");

        await ApiCall.AssertProblemAsync(response, 401, "auth_missing");
    }

    [Fact]
    public async Task AC9_MeWithTamperedInitData_Returns401AuthInvalid()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var raw = TestInitData.Replace(TestInitData.For(42, factory.Clock.UtcNow), "user", TestInitData.UserJson(1, "Admin"));

        var response = await ApiCall.GetAsync(client, "/api/platform/me", ApiCall.Tma(raw));

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    // ---------------- AC-10 ----------------

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/demo/does-not-exist")]
    [InlineData("/api/platform/does-not-exist")]
    [InlineData("/api/a/b/c/d")]
    public async Task AC10_UnknownApiRouteWithValidAuth_Returns404ProblemJsonNotFound(string path)
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, path, 42);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ApiCall.AssertProblemAsync(response, 404, "not_found");
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.DoesNotContain("<html", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/platform/unknown")]
    [InlineData("/api/nosuchmodule/x")]
    [InlineData("/api/does-not-exist")]
    public async Task AC10_UnknownApiRouteWithoutAuthorization_Returns401AuthMissingNot404(string path)
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsync(host.Client, path);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await ApiCall.AssertProblemAsync(response, 401, "auth_missing");
    }

    [Theory]
    [InlineData("/api/platform/unknown")]
    [InlineData("/api/nosuchmodule/x")]
    public async Task AC10_UnknownApiRouteWithInvalidInitData_Returns401AuthInvalidNot404(string path)
    {
        await using var host = await DemoHost.StartAsync();
        var tampered = TestInitData.FlipHash(TestInitData.For(42, host.Clock.UtcNow));

        var response = await ApiCall.GetAsync(host.Client, path, ApiCall.Tma(tampered));

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    [Theory]
    [InlineData("/api/platform/unknown")]
    [InlineData("/api/nosuchmodule/x")]
    public async Task AC10_UnknownApiRouteWithValidAuth_Returns404NotFound(string path)
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, path, 42);

        await ApiCall.AssertProblemAsync(response, 404, "not_found");
    }

    [Fact]
    public async Task AC10_UnknownApiRouteWithPostMethod_StillReturnsJson404()
    {
        await using var host = await DemoHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo/nothing");
        request.Headers.TryAddWithoutValidation("Authorization", ApiCall.Tma(TestInitData.For(42)));

        var response = await host.Client.SendAsync(request);

        await ApiCall.AssertProblemAsync(response, 404, "not_found");
    }

    // ---------------- ApiErrors contract (spec "Формат ошибок") ----------------

    [Fact]
    public async Task ApiErrors_Validation_Returns400ValidationFailedWithErrors()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/validation", 42);

        var body = await ApiCall.AssertProblemAsync(response, 400, "validation_failed");
        var messages = body.GetProperty("errors").GetProperty("title").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["too long", "bad chars"], messages);
    }

    [Fact]
    public async Task ApiErrors_Forbidden_Returns403Forbidden()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/forbidden", 42);

        await ApiCall.AssertProblemAsync(response, 403, "forbidden");
    }

    [Fact]
    public async Task ApiErrors_NotFoundWithModuleCode_Returns404WithThatCodeAndTitle()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/custom-not-found", 42);

        var body = await ApiCall.AssertProblemAsync(response, 404, "demo_thing_not_found");
        Assert.Equal("Thing not found", body.GetProperty("title").GetString());
    }

    // ---------------- AC-11 ----------------

    [Fact]
    public async Task AC11_ModuleHandlerThrows_Returns500InternalErrorWithoutDetails()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/boom", 42);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(500, (int)response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-boom-detail", text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain("   at ", text);
        Assert.DoesNotContain("DemoModule", text);
    }

    [Fact]
    public async Task AC11_ModuleHandlerThrows_ExceptionIsLogged()
    {
        await using var host = await DemoHost.StartAsync();

        await ApiCall.GetAsUserAsync(host.Client, "/api/demo/boom", 42);

        Assert.Contains(host.Logs.Entries, e =>
            e.Level >= LogLevel.Error && e.Exception is not null && e.FullText.Contains("secret-boom-detail"));
    }

    [Fact]
    public async Task AC11_AfterFailedRequest_HostKeepsServingRequests()
    {
        await using var host = await DemoHost.StartAsync();

        await ApiCall.GetAsUserAsync(host.Client, "/api/demo/boom", 42);
        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/demo/ping", 42);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------- AC-12 ----------------

    [Fact]
    public void AC12_BotTokenMissing_StartupFailsNamingTheKey()
    {
        using var factory = new PlatformFactory { Token = null };

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Bot:Token", ex.ToString());
    }

    [Fact]
    public void AC12_BotTokenEmpty_StartupFailsNamingTheKey()
    {
        using var factory = new PlatformFactory { Token = "" };

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Bot:Token", ex.ToString());
    }

    [Fact]
    public void AC12_BotTokenWhitespace_StartupFailsNamingTheKey()
    {
        using var factory = new PlatformFactory { Token = "   " };

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Bot:Token", ex.ToString());
    }

    [Fact]
    public void AC12_StartupErrorForSecretToken_DoesNotContainTokenValue()
    {
        // an invalid *other* option must not echo the token either
        using var factory = new PlatformFactory { Token = "999999:VERY-SECRET-VALUE" };
        factory.Settings["Auth:InitDataMaxAge"] = "-01:00:00";

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.DoesNotContain("VERY-SECRET-VALUE", ex.ToString());
    }

    // ---------------- AC-13 ----------------

    private const string TokenSecretPart = "TEST-TOKEN";

    private static async Task<List<string>> ExerciseAsync(HttpClient client, DateTimeOffset now)
    {
        var bodies = new List<string>();
        var requests = new[]
        {
            ApiCall.Tma(TestInitData.For(42, now)),                                                 // valid
            ApiCall.Tma(TestInitData.FlipHash(TestInitData.For(42, now))),                          // bad hash
            ApiCall.Tma(TestInitData.For(42, now - TimeSpan.FromDays(3))),                          // expired
            ApiCall.Tma(TestInitData.For(42, now, token: TestInitData.OtherBotToken)),              // other bot
            "tma garbage",
            "Bearer " + TestInitData.BotToken,                                                      // token sent as credential
            null!,                                                                                  // no header
        };
        foreach (var authorization in requests)
        {
            foreach (var path in new[] { "/api/platform/me", "/api/demo/whoami", "/api/demo/boom", "/api/unknown", "/health" })
            {
                var response = await ApiCall.GetAsync(client, path, authorization);
                bodies.Add(await response.Content.ReadAsStringAsync());
                bodies.Add(response.ToString());   // status line + all headers
            }
        }

        return bodies;
    }

    [Fact]
    public async Task AC13_ValidAndInvalidRequests_TokenNeverInLogsOrResponses()
    {
        await using var host = await DemoHost.StartAsync();

        var bodies = await ExerciseAsync(host.Client, host.Clock.UtcNow);

        Assert.NotEmpty(host.Logs.Entries);
        Assert.DoesNotContain(host.Logs.Entries, e => e.FullText.Contains(TestInitData.BotToken) || e.FullText.Contains(TokenSecretPart));
        Assert.DoesNotContain(bodies, b => b.Contains(TestInitData.BotToken) || b.Contains(TokenSecretPart));
    }

    [Fact]
    public async Task AC13_RealProgramStartupAndRequests_TokenNeverInLogsOrResponses()
    {
        await using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var bodies = await ExerciseAsync(client, factory.Clock.UtcNow);

        Assert.NotEmpty(factory.Logs.Entries);
        Assert.DoesNotContain(factory.Logs.Entries, e => e.FullText.Contains(TestInitData.BotToken) || e.FullText.Contains(TokenSecretPart));
        Assert.DoesNotContain(bodies, b => b.Contains(TestInitData.BotToken) || b.Contains(TokenSecretPart));
    }

    [Fact]
    public async Task AC13_RejectedRequests_RawInitDataAndHashNotLogged()
    {
        // BE-4: only the rejection reason is logged, never raw initData
        await using var host = await DemoHost.StartAsync();
        var valid = TestInitData.For(42, host.Clock.UtcNow, firstName: "UniqueFirstNameMarker");
        var tampered = TestInitData.FlipHash(valid);
        var hash = TestInitData.GetField(tampered, "hash");

        await ApiCall.GetAsync(host.Client, "/api/demo/ping", ApiCall.Tma(tampered));
        await ApiCall.GetAsync(host.Client, "/api/demo/ping", ApiCall.Tma(valid));

        Assert.DoesNotContain(host.Logs.Entries, e => e.FullText.Contains(hash)
            || e.FullText.Contains(TestInitData.GetField(valid, "hash"))
            || e.FullText.Contains("UniqueFirstNameMarker"));
    }
}
