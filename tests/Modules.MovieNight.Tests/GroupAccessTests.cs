using System.Net;
using System.Text.Json;
using Bot.Platform.Tests.Helpers;
using Modules.MovieNight.Tests.Helpers;

namespace Modules.MovieNight.Tests;

// TEST-3: AC-7, AC-8 (+ 401 на каждом endpoint)
public class GroupAccessTests
{
    private const string NotFoundTitle = "Группа не найдена.";
    private static readonly string Zero = Guid.Empty.ToString("D");

    public static IEnumerable<object[]> Endpoints()
    {
        yield return ["GET", "/api/movienight/groups", null!];
        yield return ["POST", "/api/movienight/groups", """{"name":"X"}"""];
        yield return ["GET", $"/api/movienight/groups/{Guid.Empty}", null!];
        yield return ["PUT", $"/api/movienight/groups/{Guid.Empty}/name", """{"name":"X"}"""];
        yield return ["POST", $"/api/movienight/groups/{Guid.Empty}/invite", null!];
        yield return ["DELETE", $"/api/movienight/groups/{Guid.Empty}/members/2", null!];
        yield return ["POST", $"/api/movienight/groups/{Guid.Empty}/leave", null!];
        yield return ["GET", "/api/movienight/invites/AB12cd34EF", null!];
        yield return ["POST", "/api/movienight/invites/AB12cd34EF/join", null!];
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AC1_EveryEndpointWithoutAuthorization_Returns401AuthMissing(string method, string path, string? body)
    {
        await using var f = new MovieNightFactory();

        var response = await f.SendAsync(new HttpMethod(method), path, null, body);

        await ApiCall.AssertProblemAsync(response, 401, "auth_missing");
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AC1_EveryEndpointWithInitDataOfOtherBot_Returns401AuthInvalid(string method, string path, string? body)
    {
        await using var f = new MovieNightFactory();
        var wrongToken = TestInitData.For(1, token: TestInitData.OtherBotToken);

        var response = await f.SendAsync(new HttpMethod(method), path, null, body, authorization: ApiCall.Tma(wrongToken));

        await ApiCall.AssertProblemAsync(response, 401, "auth_invalid");
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    // ---------------- AC-7 ----------------

    [Fact]
    public async Task AC7_ThreeMembers_ViewFromSecondShowsOrderFlagsNamesAndInvite()
    {
        await using var f = new MovieNightFactory();
        await f.SeedAsync(1, "Аня", null, "anya");
        await f.SeedAsync(2, "Петя", "Иванов", "petya");
        // 3 — нет в реестре
        var t0 = TestInitData.DefaultNow;
        f.Clock.UtcNow = t0;
        f.Codes.Enqueue("AB12cd34EF");
        var g = await f.CreateGroupAsync(1, "Мы с Петей");
        var id = g.GetProperty("id").GetString()!;
        f.Clock.UtcNow = t0.AddMinutes(1);
        await f.JoinAsync(2, "AB12cd34EF");
        f.Clock.UtcNow = t0.AddMinutes(2);
        await f.JoinAsync(3, "AB12cd34EF");

        var response = await f.GetAsync(2, $"/api/movienight/groups/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiCall.JsonAsync(response);
        Assert.Equal(id, body.GetProperty("id").GetString());
        Assert.Equal(1, body.GetProperty("ownerId").GetInt64());
        Assert.False(body.GetProperty("isOwner").GetBoolean());
        var members = body.GetProperty("members").EnumerateArray().ToArray();
        Assert.Equal([1L, 2L, 3L], members.Select(m => m.GetProperty("userId").GetInt64()).ToArray());
        Assert.Equal([1, 2, 3], members.Select(m => m.GetProperty("position").GetInt32()).ToArray());
        Assert.Equal([false, true, false], members.Select(m => m.GetProperty("isMe").GetBoolean()).ToArray());
        Assert.Equal([true, false, false], members.Select(m => m.GetProperty("isOwner").GetBoolean()).ToArray());
        Assert.Equal(["Аня", "Петя Иванов", "Участник"], members.Select(m => m.GetProperty("displayName").GetString()!).ToArray());
        Assert.Equal("anya", members[0].GetProperty("username").GetString());
        Assert.Equal("petya", members[1].GetProperty("username").GetString());
        Assert.Equal(JsonValueKind.Null, members[2].GetProperty("username").ValueKind);
        Assert.Equal([t0, t0.AddMinutes(1), t0.AddMinutes(2)], members.Select(m => m.GetProperty("joinedAt").GetDateTimeOffset()).ToArray());
        Assert.Equal("AB12cd34EF", body.GetProperty("invite").GetProperty("code").GetString());
        Assert.Equal("https://t.me/MovieNightBot/app?startapp=movienight_join_AB12cd34EF",
            body.GetProperty("invite").GetProperty("miniAppLink").GetString());
    }

    [Fact]
    public async Task AC7_RegistryNameBlankOrWhitespace_DisplayNameIsParticipantButUsernameKept()
    {
        await using var f = new MovieNightFactory();
        await f.SeedAsync(1, "Аня");
        await f.SeedAsync(2, "   ", null, "ghost");
        var (id, _) = await f.GroupWithAsync("G", 1, 2);

        var body = await f.GetGroupAsync(1, id);

        var m = body.GetProperty("members")[1];
        Assert.Equal("Участник", m.GetProperty("displayName").GetString());
        Assert.Equal("ghost", m.GetProperty("username").GetString());
    }

    [Fact]
    public async Task AC7_ViewByOwner_FlagsAreOwnerSpecific()
    {
        await using var f = new MovieNightFactory();
        var (id, _) = await f.GroupWithAsync("G", 1, 2);

        var body = await f.GetGroupAsync(1, id);

        Assert.True(body.GetProperty("isOwner").GetBoolean());
        Assert.True(body.GetProperty("members")[0].GetProperty("isMe").GetBoolean());
        Assert.False(body.GetProperty("members")[1].GetProperty("isMe").GetBoolean());
    }

    // ---------------- AC-8 ----------------

    [Fact]
    public async Task AC8_NonMemberOnAnyGroupEndpoint_Returns404GroupNotFound()
    {
        await using var f = new MovieNightFactory();
        var (id, _) = await f.GroupWithAsync("Чужая", 1, 2);
        var name = f.ScalarString("SELECT Name FROM movienight_groups");
        var codeBefore = f.ScalarString("SELECT InviteCode FROM movienight_groups");

        var calls = new[]
        {
            await f.GetAsync(9, $"/api/movienight/groups/{id}"),
            await f.PutAsync(9, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody("Взлом")),
            await f.PostAsync(9, $"/api/movienight/groups/{id}/invite"),
            await f.DeleteAsync(9, $"/api/movienight/groups/{id}/members/2"),
            await f.PostAsync(9, $"/api/movienight/groups/{id}/leave"),
        };

        foreach (var r in calls)
            await MovieNightFactory.AssertGroupProblemAsync(r, 404, "movienight_group_not_found", NotFoundTitle);
        Assert.Equal(name, f.ScalarString("SELECT Name FROM movienight_groups"));
        Assert.Equal(codeBefore, f.ScalarString("SELECT InviteCode FROM movienight_groups"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC8_NonexistentGroup_Returns404WithSameBodyAsForeignGroup()
    {
        await using var f = new MovieNightFactory();
        var (foreign, _) = await f.GroupWithAsync("Чужая", 1);

        var missing = await f.GetAsync(9, $"/api/movienight/groups/{Zero}");
        var existing = await f.GetAsync(9, $"/api/movienight/groups/{foreign}");

        await MovieNightFactory.AssertGroupProblemAsync(missing, 404, "movienight_group_not_found", NotFoundTitle);
        await MovieNightFactory.AssertGroupProblemAsync(existing, 404, "movienight_group_not_found", NotFoundTitle);
    }

    [Fact]
    public async Task AC8_NonexistentGroupOnEveryGroupEndpoint_Returns404()
    {
        await using var f = new MovieNightFactory();
        var r = new[]
        {
            await f.PutAsync(1, $"/api/movienight/groups/{Zero}/name", MovieNightFactory.NameBody("X")),
            await f.PostAsync(1, $"/api/movienight/groups/{Zero}/invite"),
            await f.DeleteAsync(1, $"/api/movienight/groups/{Zero}/members/2"),
            await f.PostAsync(1, $"/api/movienight/groups/{Zero}/leave"),
        };

        foreach (var x in r)
            await MovieNightFactory.AssertGroupProblemAsync(x, 404, "movienight_group_not_found", NotFoundTitle);
    }

    [Theory]
    [InlineData("GET", "/api/movienight/groups/abc", null)]
    [InlineData("PUT", "/api/movienight/groups/abc/name", """{"name":"X"}""")]
    [InlineData("POST", "/api/movienight/groups/abc/invite", null)]
    [InlineData("DELETE", "/api/movienight/groups/abc/members/2", null)]
    [InlineData("POST", "/api/movienight/groups/abc/leave", null)]
    [InlineData("GET", "/api/movienight/groups/1234/", null)]
    public async Task AC8_IdNotGuid_Returns404NotFoundFromPlatformFallback(string method, string path, string? body)
    {
        await using var f = new MovieNightFactory();

        var response = await f.SendAsync(new HttpMethod(method), path, 1, body);

        await ApiCall.AssertProblemAsync(response, 404, "not_found");
    }
}
