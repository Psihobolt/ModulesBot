using System.Globalization;
using System.Net;
using System.Text.Json;
using Bot.Platform.Tests.Helpers;
using Modules.MovieNight.Tests.Helpers;

namespace Modules.MovieNight.Tests;

// TEST-2: AC-2..AC-6
public class CreateGroupTests
{
    private const string NameError = "Название — от 1 до 64 символов, без переводов строк.";
    private const string LimitTitle = "Можно состоять не больше чем в 20 группах.";

    private static string Name(int length) => new('Я', length);

    // ---------------- AC-2 ----------------

    [Fact]
    public async Task AC2_ValidNameWithSpaces_Returns201WithTrimmedNameOwnerMemberAndLinks()
    {
        await using var f = new MovieNightFactory();
        await f.SeedAsync(1, "Аня", null, "anya");
        f.Codes.Enqueue("AB12cd34EF");
        var now = TestInitData.DefaultNow.AddMinutes(7);
        f.Clock.UtcNow = now;

        var response = await f.PostAsync(1, "/api/movienight/groups", MovieNightFactory.NameBody("  Мы с Петей  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var g = await ApiCall.JsonAsync(response);
        var id = g.GetProperty("id").GetString()!;
        Assert.True(Guid.TryParseExact(id, "D", out _));
        Assert.Equal("Мы с Петей", g.GetProperty("name").GetString());
        Assert.Equal(1, g.GetProperty("ownerId").GetInt64());
        Assert.True(g.GetProperty("isOwner").GetBoolean());
        Assert.Equal(now, g.GetProperty("createdAt").GetDateTimeOffset());
        var members = g.GetProperty("members").EnumerateArray().ToArray();
        var m = Assert.Single(members);
        Assert.Equal(1, m.GetProperty("userId").GetInt64());
        Assert.Equal(1, m.GetProperty("position").GetInt32());
        Assert.True(m.GetProperty("isMe").GetBoolean());
        Assert.True(m.GetProperty("isOwner").GetBoolean());
        Assert.Equal("Аня", m.GetProperty("displayName").GetString());
        Assert.Equal("anya", m.GetProperty("username").GetString());
        Assert.Equal(now, m.GetProperty("joinedAt").GetDateTimeOffset());
        var invite = g.GetProperty("invite");
        Assert.Equal("AB12cd34EF", invite.GetProperty("code").GetString());
        Assert.Equal("https://t.me/MovieNightBot/app?startapp=movienight_join_AB12cd34EF", invite.GetProperty("miniAppLink").GetString());
        Assert.Equal("https://t.me/MovieNightBot?start=movienight_join_AB12cd34EF", invite.GetProperty("botLink").GetString());
        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        Assert.Equal($"/api/movienight/groups/{id}", path);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Fact]
    public async Task AC2_DefaultGenerator_ProducesTenCharacterAlphanumericCode()
    {
        await using var f = new MovieNightFactory();

        var g = await f.CreateGroupAsync(1);

        Assert.Matches("^[A-Za-z0-9]{10}$", g.GetProperty("invite").GetProperty("code").GetString()!);
    }

    [Fact]
    public async Task AC2_UserNotInRegistry_MemberDisplayNameIsParticipantAndUsernameNull()
    {
        await using var f = new MovieNightFactory();

        var g = await f.CreateGroupAsync(1);

        var m = g.GetProperty("members")[0];
        Assert.Equal("Участник", m.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, m.GetProperty("username").ValueKind);
    }

    [Fact]
    public async Task AC2_NoBotUsername_BothLinksNull()
    {
        await using var f = new MovieNightFactory { Username = null };

        var g = await f.CreateGroupAsync(1);

        var invite = g.GetProperty("invite");
        Assert.Equal(JsonValueKind.Null, invite.GetProperty("miniAppLink").ValueKind);
        Assert.Equal(JsonValueKind.Null, invite.GetProperty("botLink").ValueKind);
        Assert.Matches("^[A-Za-z0-9]{10}$", invite.GetProperty("code").GetString()!);
    }

    [Fact]
    public async Task AC2_NoMiniAppShortName_MiniAppLinkLeadsToMainMiniApp()
    {
        await using var f = new MovieNightFactory { MiniAppShortName = null };
        f.Codes.Enqueue("AB12cd34EF");

        var g = await f.CreateGroupAsync(1);

        var invite = g.GetProperty("invite");
        Assert.Equal("https://t.me/MovieNightBot?startapp=movienight_join_AB12cd34EF", invite.GetProperty("miniAppLink").GetString());
        Assert.Equal("https://t.me/MovieNightBot?start=movienight_join_AB12cd34EF", invite.GetProperty("botLink").GetString());
    }

    [Fact]
    public async Task AC2_InvalidInitData_Returns401AndNothingCreated()
    {
        await using var f = new MovieNightFactory();

        var tampered = TestInitData.FlipHash(TestInitData.For(1));
        var response = await f.SendAsync(HttpMethod.Post, "/api/movienight/groups", null, MovieNightFactory.NameBody("X"),
            authorization: ApiCall.Tma(tampered));

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    // ---------------- AC-3 ----------------

    public static IEnumerable<object[]> InvalidNameBodies()
    {
        yield return ["{}"];
        yield return ["""{"name":null}"""];
        yield return ["""{"name":123}"""];
        yield return ["""{"name":true}"""];
        yield return ["""{"name":["a"]}"""];
        yield return ["""{"name":""}"""];
        yield return ["""{"name":"   "}"""];
        yield return [JsonSerializer.Serialize(new { name = Name(65) })];
        yield return [JsonSerializer.Serialize(new { name = "  " + Name(65) + "  " })];
        yield return [JsonSerializer.Serialize(new { name = "a\nb" })];
        yield return [JsonSerializer.Serialize(new { name = "a\rb" })];
        yield return [JsonSerializer.Serialize(new { name = "a\tb" })];
        yield return [JsonSerializer.Serialize(new { name = "a\u0000b" })];
        yield return [JsonSerializer.Serialize(new { name = "a\u007Fb" })];
        yield return [JsonSerializer.Serialize(new { name = "\u0085" })];
    }

    [Theory]
    [MemberData(nameof(InvalidNameBodies))]
    public async Task AC3_InvalidName_Returns400ValidationFailedAndNothingCreated(string body)
    {
        await using var f = new MovieNightFactory();

        var response = await f.PostAsync(1, "/api/movienight/groups", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ApiCall.JsonAsync(response);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal("Validation failed", problem.GetProperty("title").GetString());
        Assert.Equal(NameError, problem.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC3_NameOfExactly64Characters_Accepted()
    {
        await using var f = new MovieNightFactory();

        var g = await f.CreateGroupAsync(1, Name(64));

        Assert.Equal(Name(64), g.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AC3_Name64CharactersPlusSurroundingSpaces_TrimmedAndAccepted()
    {
        await using var f = new MovieNightFactory();

        var g = await f.CreateGroupAsync(1, "   " + Name(64) + "  ");

        Assert.Equal(Name(64), g.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AC3_NameWithInnerSpacesAndPunctuation_Accepted()
    {
        await using var f = new MovieNightFactory();

        var g = await f.CreateGroupAsync(1, "Кино  вечер: «топ» 🎬");

        Assert.Equal("Кино  вечер: «топ» 🎬", g.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"name\":")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"str\"")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    public async Task AC3_BodyNotJsonObject_Returns400BadRequestAndNothingCreated(string body)
    {
        await using var f = new MovieNightFactory();

        var response = await f.PostAsync(1, "/api/movienight/groups", body);

        var problem = await ApiCall.AssertProblemAsync(response, 400, "bad_request");
        Assert.Equal("Request body must be a JSON object.", problem.GetProperty("title").GetString());
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Fact]
    public async Task AC3_NoBodyAtAll_Returns400AndNothingCreated()
    {
        await using var f = new MovieNightFactory();

        var response = await f.PostAsync(1, "/api/movienight/groups");

        var problem = await ApiCall.AssertProblemAsync(response, 400, "bad_request");
        Assert.Equal("Request body must be a JSON object.", problem.GetProperty("title").GetString());
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    // ---------------- AC-4 ----------------

    private static async Task Create(MovieNightFactory f, long user, int count)
    {
        for (var i = 0; i < count; i++) await f.CreateGroupAsync(user, $"G{i}");
    }

    [Fact]
    public async Task AC4_UserIn19Groups_CreateSucceedsAndBecomes20()
    {
        await using var f = new MovieNightFactory();
        await Create(f, 1, 19);

        var response = await f.PostAsync(1, "/api/movienight/groups", MovieNightFactory.NameBody("Двадцатая"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 1"));
    }

    [Fact]
    public async Task AC4_UserIn20Groups_CreateReturns409AndNothingChanges()
    {
        await using var f = new MovieNightFactory();
        await Create(f, 1, 20);

        var response = await f.PostAsync(1, "/api/movienight/groups", MovieNightFactory.NameBody("Лишняя"));

        await MovieNightFactory.AssertGroupProblemAsync(response, 409, "movienight_group_limit", LimitTitle);
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC4_UserIn20Groups_JoinNewGroupReturns409AndNotAdded()
    {
        await using var f = new MovieNightFactory();
        await Create(f, 1, 20);
        var other = await f.CreateGroupAsync(2, "Чужая");
        var code = other.GetProperty("invite").GetProperty("code").GetString()!;

        var response = await f.PostAsync(1, $"/api/movienight/invites/{code}/join");

        await MovieNightFactory.AssertGroupProblemAsync(response, 409, "movienight_group_limit", LimitTitle);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE GroupId IN (SELECT Id FROM movienight_groups WHERE Name = 'Чужая')"));
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 1"));
    }

    [Fact]
    public async Task AC4_UserIn19Groups_JoinSucceedsAndBecomes20()
    {
        await using var f = new MovieNightFactory();
        await Create(f, 1, 19);
        var other = await f.CreateGroupAsync(2, "Чужая");

        var joined = await f.JoinAsync(1, other.GetProperty("invite").GetProperty("code").GetString()!);

        Assert.True(joined.GetProperty("joined").GetBoolean());
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 1"));
    }

    [Fact]
    public async Task AC4_UserIn20GroupsAlreadyMemberOfTarget_JoinReturns200JoinedFalse()
    {
        await using var f = new MovieNightFactory();
        var target = await f.CreateGroupAsync(2, "Целевая");
        var code = target.GetProperty("invite").GetProperty("code").GetString()!;
        await f.JoinAsync(1, code);
        await Create(f, 1, 19);
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 1"));

        var result = await f.JoinAsync(1, code);

        Assert.False(result.GetProperty("joined").GetBoolean());
    }

    // ---------------- AC-5 ----------------

    [Fact]
    public async Task AC5_JoinedAThenBWithSameClock_ListIsBThenAWithCountsAndOwnerFlags()
    {
        await using var f = new MovieNightFactory();
        var a = await f.CreateGroupAsync(2, "A");
        var b = await f.CreateGroupAsync(3, "B");
        await f.CreateGroupAsync(4, "C");
        await f.JoinAsync(1, a.GetProperty("invite").GetProperty("code").GetString()!);
        await f.JoinAsync(1, b.GetProperty("invite").GetProperty("code").GetString()!);

        var response = await f.GetAsync(1, "/api/movienight/groups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await ApiCall.JsonAsync(response)).EnumerateArray().ToArray();
        Assert.Equal(["B", "A"], list.Select(x => x.GetProperty("name").GetString()!).ToArray());
        Assert.All(list, x =>
        {
            Assert.Equal(2, x.GetProperty("memberCount").GetInt32());
            Assert.False(x.GetProperty("isOwner").GetBoolean());
        });
        Assert.Equal(b.GetProperty("id").GetString(), list[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AC5_OwnCreatedAndJoinedGroups_OrderedByOwnJoinNewestFirstWithOwnerFlag()
    {
        await using var f = new MovieNightFactory();
        await f.CreateGroupAsync(1, "G1");
        var g2 = await f.CreateGroupAsync(2, "G2");
        await f.JoinAsync(1, g2.GetProperty("invite").GetProperty("code").GetString()!);
        await f.CreateGroupAsync(1, "G3");

        var list = (await ApiCall.JsonAsync(await f.GetAsync(1, "/api/movienight/groups"))).EnumerateArray().ToArray();

        Assert.Equal(["G3", "G2", "G1"], list.Select(x => x.GetProperty("name").GetString()!).ToArray());
        Assert.Equal([true, false, true], list.Select(x => x.GetProperty("isOwner").GetBoolean()).ToArray());
    }

    [Fact]
    public async Task AC5_NoGroups_ReturnsEmptyArray()
    {
        await using var f = new MovieNightFactory();
        await f.CreateGroupAsync(2, "Чужая");

        var response = await f.GetAsync(1, "/api/movienight/groups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiCall.JsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    // ---------------- AC-6 ----------------

    [Fact]
    public async Task AC6_GeneratorReturnsCodeOfActiveGroup_NextCodeUsed()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AAAAAAAAAA", "AAAAAAAAAA", "BBBBBBBBBB");
        await f.CreateGroupAsync(1, "Первая");

        var second = await f.CreateGroupAsync(2, "Вторая");

        Assert.Equal("BBBBBBBBBB", second.GetProperty("invite").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC6_GeneratorReturnsRevokedCode_ItIsNeverReissued()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("OLDcode123", "NEWcode123");
        var g = await f.CreateGroupAsync(1, "Первая");
        var id = g.GetProperty("id").GetString();
        var regenerated = await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);

        f.Codes.Enqueue("OLDcode123", "FRESHcod12");
        var second = await f.CreateGroupAsync(2, "Вторая");

        Assert.Equal("FRESHcod12", second.GetProperty("invite").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC6_RegenerateWhenGeneratorReturnsOccupiedCodes_SkipsToFreeCode()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AAAAAAAAAA", "BBBBBBBBBB");
        var first = await f.CreateGroupAsync(1, "Первая");
        await f.CreateGroupAsync(2, "Вторая");
        var id = first.GetProperty("id").GetString();
        f.Codes.Enqueue("BBBBBBBBBB", "AAAAAAAAAA", "CCCCCCCCCC");

        var response = await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CCCCCCCCCC", (await ApiCall.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC6_FiveCollisionsOnCreate_Returns500InternalErrorAndNothingChanges()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AAAAAAAAAA");
        await f.CreateGroupAsync(1, "Первая");
        var callsBefore = f.Codes.Calls;
        f.Codes.Enqueue("AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA");

        var response = await f.PostAsync(2, "/api/movienight/groups", MovieNightFactory.NameBody("Вторая"));

        await ApiCall.AssertProblemAsync(response, 500, "internal_error");
        Assert.Equal(5, f.Codes.Calls - callsBefore);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC6_FourCollisionsThenFreeCode_FifthAttemptSucceeds()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AAAAAAAAAA");
        await f.CreateGroupAsync(1, "Первая");
        f.Codes.Enqueue("AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA", "ZZZZZZZZZZ");

        var second = await f.CreateGroupAsync(2, "Вторая");

        Assert.Equal("ZZZZZZZZZZ", second.GetProperty("invite").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC6_FiveCollisionsOnRegenerate_Returns500AndCodeUnchanged()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AAAAAAAAAA");
        var g = await f.CreateGroupAsync(1, "Первая");
        var id = g.GetProperty("id").GetString();
        f.Codes.Enqueue("AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA", "AAAAAAAAAA");

        var response = await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");

        await ApiCall.AssertProblemAsync(response, 500, "internal_error");
        Assert.Equal("AAAAAAAAAA", f.ScalarString("SELECT InviteCode FROM movienight_groups"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes"));
    }
}
