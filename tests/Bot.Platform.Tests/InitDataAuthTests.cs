using System.Net;
using Bot.Platform.Auth;
using Bot.Platform.Tests.Helpers;

namespace Bot.Platform.Tests;

// TEST-3: AC-5..AC-8 (HTTP integration + IInitDataValidator unit-level)
public class InitDataAuthTests
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    private static Task<HttpResponseMessage> Whoami(DemoHost host, string rawInitData) =>
        ApiCall.GetAsync(host.Client, "/api/demo/whoami", ApiCall.Tma(rawInitData));

    // ---------------- AC-5 ----------------

    [Fact]
    public async Task AC5_ValidFreshInitData_PassesAndGetInitDataReturnsUserAuthDateStartParam()
    {
        await using var host = await DemoHost.StartAsync();
        var authDate = host.Clock.UtcNow - TimeSpan.FromHours(1);
        var raw = TestInitData.For(
            42, authDate, startParam: "movienight_join_AB12",
            extra: new Dictionary<string, string> { ["chat_type"] = "private", ["chat_instance"] = "-123456" },
            firstName: "Иван", lastName: "Петров", username: "ivan", languageCode: "ru");

        var response = await Whoami(host, raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiCall.JsonAsync(response);
        Assert.Equal(42, body.GetProperty("id").GetInt64());
        Assert.Equal("Иван", body.GetProperty("firstName").GetString());
        Assert.Equal("Петров", body.GetProperty("lastName").GetString());
        Assert.Equal("ivan", body.GetProperty("username").GetString());
        Assert.Equal("ru", body.GetProperty("languageCode").GetString());
        Assert.Equal(authDate.ToUnixTimeSeconds(), body.GetProperty("authDate").GetInt64());
        Assert.Equal("movienight_join_AB12", body.GetProperty("startParam").GetString());
        Assert.Equal("private", body.GetProperty("chatType").GetString());
        Assert.Equal("-123456", body.GetProperty("chatInstance").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("queryId").GetString()));
    }

    [Fact]
    public async Task AC5_NoStartParam_GetInitDataStartParamIsNull()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, TestInitData.For(42));

        var body = await ApiCall.JsonAsync(response);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("startParam").ValueKind);
    }

    [Fact]
    public async Task AC5_AuthDateExactlyMaxAgeOld_Passes()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow - MaxAge));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC5_CustomMaxAgeFromConfiguration_IsRespected()
    {
        await using var host = await DemoHost.StartAsync(new Dictionary<string, string?> { ["Auth:InitDataMaxAge"] = "00:10:00" });

        var ok = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow - TimeSpan.FromMinutes(10)));
        var expired = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow - TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1)));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await ApiCall.AssertProblemAsync(expired, 401, "auth_expired");
    }

    [Fact]
    public async Task AC5_ValidInitDataOnPlatformEndpoint_AlsoPasses()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await ApiCall.GetAsUserAsync(host.Client, "/api/platform/me", 42);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC5_HashInUpperCaseHex_Passes()
    {
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.For(42);
        var upper = TestInitData.Replace(raw, "hash", TestInitData.GetField(raw, "hash").ToUpperInvariant());

        var response = await Whoami(host, upper);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC5_SignatureFieldIncludedInDataCheckString_Passes()
    {
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.For(42, extra: new Dictionary<string, string> { ["signature"] = "c2lnbmF0dXJlLXZhbHVl" });

        var response = await Whoami(host, raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC5_SignatureFieldExcludedWhenSigning_IsRejected()
    {
        await using var host = await DemoHost.StartAsync();
        var fields = TestInitData.Fields(42, extra: new Dictionary<string, string> { ["signature"] = "c2ln" });
        var raw = TestInitData.Sign(fields, TestInitData.BotToken, "signature");

        var response = await Whoami(host, raw);

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    // ---------------- AC-6 ----------------

    [Theory]
    [InlineData("user")]
    [InlineData("auth_date")]
    [InlineData("start_param")]
    [InlineData("query_id")]
    [InlineData("hash")]
    public async Task AC6_FieldChangedAfterSigning_Returns401AuthInvalid(string field)
    {
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.For(42, startParam: "movienight_join_AB12");
        var tampered = field switch
        {
            "user" => TestInitData.Replace(raw, "user", TestInitData.UserJson(43, "Test", "User", "tester", "ru")),
            "auth_date" => TestInitData.Replace(raw, "auth_date",
                (long.Parse(TestInitData.GetField(raw, "auth_date"), System.Globalization.CultureInfo.InvariantCulture) + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "start_param" => TestInitData.Replace(raw, "start_param", "movienight_join_ZZZZ"),
            "query_id" => TestInitData.Replace(raw, "query_id", "AAHother"),
            _ => TestInitData.FlipHash(raw),
        };
        Assert.NotEqual(raw, tampered);

        var response = await Whoami(host, tampered);

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    [Fact]
    public async Task AC6_ExtraFieldAddedAfterSigning_Returns401AuthInvalid()
    {
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.For(42) + "&start_param=evil";

        var response = await Whoami(host, raw);

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    // ---------------- AC-7 ----------------

    [Fact]
    public async Task AC7_AuthDateOlderThanMaxAgeBy1Second_Returns401AuthExpired()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow - MaxAge - TimeSpan.FromSeconds(1)));

        await ApiCall.AssertProblemAsync(response, 401, "auth_expired");
    }

    [Fact]
    public async Task AC7_ClockMovesForward_PreviouslyValidInitDataBecomesExpired()
    {
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.For(42, host.Clock.UtcNow);

        var before = await Whoami(host, raw);
        host.Clock.Advance(MaxAge + TimeSpan.FromSeconds(1));
        var after = await Whoami(host, raw);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await ApiCall.AssertProblemAsync(after, 401, "auth_expired");
    }

    [Fact]
    public async Task AC7_AuthDateInFutureWithinSkew_Passes()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow + Skew));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC7_AuthDateInFutureBeyondSkewBy1Second_Returns401AuthInvalid()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow + Skew + TimeSpan.FromSeconds(1)));

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    [Fact]
    public async Task AC7_CustomSkewFromConfiguration_IsRespected()
    {
        await using var host = await DemoHost.StartAsync(new Dictionary<string, string?> { ["Auth:AllowedClockSkew"] = "00:00:30" });

        var ok = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow + TimeSpan.FromSeconds(30)));
        var bad = await Whoami(host, TestInitData.For(42, host.Clock.UtcNow + TimeSpan.FromSeconds(31)));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await ApiCall.AssertProblemAsync(bad, 401, "auth_invalid");
    }

    // ---------------- AC-8: each malformed variant separately ----------------

    private static string BuildAuthorization(string variant)
    {
        var valid = TestInitData.For(42);
        List<KeyValuePair<string, string>> Without(string key) =>
            TestInitData.Fields(42).Where(f => f.Key != key).ToList();
        List<KeyValuePair<string, string>> WithUser(string userJson) =>
            TestInitData.Fields(42).Select(f => f.Key == "user" ? new KeyValuePair<string, string>("user", userJson) : f).ToList();

        return variant switch
        {
            "bearer-scheme" => "Bearer " + valid,
            "basic-scheme" => "Basic " + valid,
            "scheme-only-empty-initdata" => "tma ",
            "scheme-only-no-space" => "tma",
            "no-hash" => ApiCall.Tma(TestInitData.Without(valid, "hash")),
            "no-user" => ApiCall.Tma(TestInitData.Sign(Without("user"))),
            "user-without-id" => ApiCall.Tma(TestInitData.Sign(WithUser("{\"first_name\":\"Test\"}"))),
            "user-invalid-json" => ApiCall.Tma(TestInitData.Sign(WithUser("{not json"))),
            "user-not-object" => ApiCall.Tma(TestInitData.Sign(WithUser("[1,2,3]"))),
            "no-auth-date" => ApiCall.Tma(TestInitData.Sign(Without("auth_date"))),
            "auth-date-not-a-number" => ApiCall.Tma(TestInitData.Sign(
                TestInitData.Fields(42).Select(f => f.Key == "auth_date" ? new KeyValuePair<string, string>("auth_date", "yesterday") : f).ToList())),
            "other-bot-token" => ApiCall.Tma(TestInitData.For(42, token: TestInitData.OtherBotToken)),
            "not-a-query-string" => "tma this is definitely not a query string",
            "header-too-long" => ApiCall.Tma(TestInitData.For(
                42, extra: new Dictionary<string, string> { ["padding"] = new string('x', 17_000) })),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
    }

    [Theory]
    [InlineData("bearer-scheme")]
    [InlineData("basic-scheme")]
    [InlineData("scheme-only-empty-initdata")]
    [InlineData("scheme-only-no-space")]
    [InlineData("no-hash")]
    [InlineData("no-user")]
    [InlineData("user-without-id")]
    [InlineData("user-invalid-json")]
    [InlineData("user-not-object")]
    [InlineData("no-auth-date")]
    [InlineData("auth-date-not-a-number")]
    [InlineData("other-bot-token")]
    [InlineData("not-a-query-string")]
    [InlineData("header-too-long")]
    public async Task AC8_MalformedAuthorization_Returns401AuthInvalid(string variant)
    {
        await using var host = await DemoHost.StartAsync();
        var authorization = BuildAuthorization(variant);
        if (variant == "header-too-long")
        {
            Assert.True(authorization.Length > 16_384, "test precondition: header must exceed 16 384 chars");
        }

        var response = await ApiCall.GetAsync(host.Client, "/api/demo/ping", authorization);

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
        Assert.Equal(0, host.Services.GetRequiredService<PingCounter>().Count);
    }

    [Fact]
    public async Task AC8_ValidInitDataWithPaddingJustUnderHeaderLimit_StillPasses()
    {
        // guards the "too long" test: the padding itself must not be the reason for rejection under the limit
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.For(42, extra: new Dictionary<string, string> { ["padding"] = new string('x', 2_000) });

        var response = await Whoami(host, raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string RawInitDataOfLength(int length)
    {
        string Build(int pad) => TestInitData.For(
            42, extra: new Dictionary<string, string> { ["padding"] = new string('x', pad) });

        var baseLength = Build(0).Length;
        var raw = Build(length - baseLength);
        Assert.Equal(length, raw.Length);   // test precondition: raw initData after "tma " has exactly this many chars
        return raw;
    }

    [Fact]
    public async Task AC8_SignedInitDataOfExactly16384Chars_Passes()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, RawInitDataOfLength(16_384));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC8_SignedInitDataOf16385Chars_Returns401AuthInvalid()
    {
        await using var host = await DemoHost.StartAsync();

        var response = await Whoami(host, RawInitDataOfLength(16_385));

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
        Assert.Equal(0, host.Services.GetRequiredService<PingCounter>().Count);
    }

    [Fact]
    public async Task AC8_OversizedInitData_RejectedBeforeSignatureCheck_NoValidationDetailsLeaked()
    {
        // bad hash AND too long: still auth_invalid (same code, no distinguishable detail)
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.FlipHash(RawInitDataOfLength(20_000));

        var response = await Whoami(host, raw);

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
    }

    // ---------------- IInitDataValidator (unit level through DI) ----------------

    private static IInitDataValidator ValidatorOf(DemoHost host) => host.Services.GetRequiredService<IInitDataValidator>();

    [Fact]
    public async Task AC5_ValidatorWithValidInitData_ReturnsDataWithoutError()
    {
        await using var host = await DemoHost.StartAsync();
        var authDate = host.Clock.UtcNow - TimeSpan.FromMinutes(5);
        var raw = TestInitData.For(42, authDate, "demo_abc", allowsWriteToPm: true, firstName: "Иван", lastName: null, username: null);

        var result = ValidatorOf(host).Validate(raw);

        Assert.Null(result.Error);
        Assert.NotNull(result.Data);
        Assert.Equal(42, result.Data.User.Id);
        Assert.Equal("Иван", result.Data.User.FirstName);
        Assert.Null(result.Data.User.LastName);
        Assert.Null(result.Data.User.Username);
        Assert.True(result.Data.User.AllowsWriteToPm);
        Assert.Equal(authDate, result.Data.AuthDate);
        Assert.Equal("demo_abc", result.Data.StartParam);
    }

    [Fact]
    public async Task AC5_ValidatorAllowsWriteToPmAbsent_IsNull()
    {
        await using var host = await DemoHost.StartAsync();

        var result = ValidatorOf(host).Validate(TestInitData.For(42));

        Assert.Null(result.Data!.User.AllowsWriteToPm);
    }

    [Fact]
    public async Task AC2_ValidatorWithNull_ReturnsMissing()
    {
        await using var host = await DemoHost.StartAsync();

        var result = ValidatorOf(host).Validate(null);

        Assert.Null(result.Data);
        Assert.Equal(InitDataError.Missing, result.Error);
    }

    [Fact]
    public async Task AC6_ValidatorWithTamperedUser_ReturnsInvalid()
    {
        await using var host = await DemoHost.StartAsync();
        var raw = TestInitData.Replace(TestInitData.For(42), "user", TestInitData.UserJson(1, "Admin"));

        var result = ValidatorOf(host).Validate(raw);

        Assert.Null(result.Data);
        Assert.Equal(InitDataError.Invalid, result.Error);
    }

    [Fact]
    public async Task AC7_ValidatorWithExpiredAuthDate_ReturnsExpired()
    {
        await using var host = await DemoHost.StartAsync();

        var result = ValidatorOf(host).Validate(TestInitData.For(42, host.Clock.UtcNow - MaxAge - TimeSpan.FromSeconds(1)));

        Assert.Null(result.Data);
        Assert.Equal(InitDataError.Expired, result.Error);
    }

    [Fact]
    public async Task AC8_ValidatorWithOtherBotToken_ReturnsInvalid()
    {
        await using var host = await DemoHost.StartAsync();

        var result = ValidatorOf(host).Validate(TestInitData.For(42, token: TestInitData.OtherBotToken));

        Assert.Equal(InitDataError.Invalid, result.Error);
    }

    [Fact]
    public async Task AC8_ValidatorWithEmptyString_ReturnsErrorAndNoData()
    {
        await using var host = await DemoHost.StartAsync();

        var result = ValidatorOf(host).Validate("");

        Assert.Null(result.Data);
        Assert.NotNull(result.Error);
    }
}
