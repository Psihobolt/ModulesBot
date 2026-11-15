using System.Net;
using System.Text.Json;
using Bot.Platform.Tests.Helpers;
using Modules.MovieNight.Tests.Helpers;

namespace Modules.MovieNight.Tests;

// TEST-4: AC-9..AC-14
public class OwnerActionsTests
{
    private const string NotOwnerTitle = "Это может сделать только владелец группы.";
    private const string NameError = "Название — от 1 до 64 символов, без переводов строк.";

    private static async Task<(MovieNightFactory F, string Id, string Code)> Setup(params long[] members)
    {
        var f = new MovieNightFactory();
        await f.SeedDefaultsAsync();
        f.Codes.Enqueue("OLDcode123");
        var (id, code) = await f.GroupWithAsync("Мы с Петей", 1, members);
        f.Sender.Clear();
        return (f, id, code);
    }

    // ---------------- AC-9 ----------------

    [Fact]
    public async Task AC9_NonOwnerOnOwnerActions_Returns403NotOwnerAndNothingChanges()
    {
        var (f, id, code) = await Setup(2, 3);
        await using var disposeF = f;

        var calls = new[]
        {
            await f.PutAsync(2, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody("Новое")),
            await f.PostAsync(2, $"/api/movienight/groups/{id}/invite"),
            await f.DeleteAsync(2, $"/api/movienight/groups/{id}/members/3"),
        };

        foreach (var r in calls) await MovieNightFactory.AssertGroupProblemAsync(r, 403, "movienight_not_owner", NotOwnerTitle);
        var g = await f.GetGroupAsync(1, id);
        Assert.Equal("Мы с Петей", g.GetProperty("name").GetString());
        Assert.Equal(code, g.GetProperty("invite").GetProperty("code").GetString());
        Assert.Equal([1L, 2L, 3L], MovieNightFactory.MemberIds(g));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes"));
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC9_NonMemberOnOwnerAction_Gets404BeforeOwnerCheck()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var r1 = await f.PutAsync(4, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody("Новое"));
        var r2 = await f.PostAsync(4, $"/api/movienight/groups/{id}/invite");
        var r3 = await f.DeleteAsync(4, $"/api/movienight/groups/{id}/members/2");

        foreach (var r in new[] { r1, r2, r3 })
            await MovieNightFactory.AssertGroupProblemAsync(r, 404, "movienight_group_not_found", "Группа не найдена.");
    }

    [Fact]
    public async Task AC9_NonOwnerRemovesSelf_StillForbiddenNotAllowedViaDelete()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var r = await f.DeleteAsync(2, $"/api/movienight/groups/{id}/members/2");

        await MovieNightFactory.AssertGroupProblemAsync(r, 403, "movienight_not_owner", NotOwnerTitle);
    }

    // ---------------- AC-10 ----------------

    [Fact]
    public async Task AC10_OwnerRenames_Returns200WithTrimmedNameAndNoNotifications()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var response = await f.PutAsync(1, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody("  Киноклуб "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var g = await ApiCall.JsonAsync(response);
        Assert.Equal("Киноклуб", g.GetProperty("name").GetString());
        Assert.Equal(id, g.GetProperty("id").GetString());
        Assert.Equal("Киноклуб", (await f.GetGroupAsync(2, id)).GetProperty("name").GetString());
        Assert.Empty(f.Sender.Sent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":42}""")]
    [InlineData("""{"name":"  "}""")]
    [InlineData("""{"name":"a\nb"}""")]
    [InlineData("""{"name":"a\u007Fb"}""")]
    public async Task AC10_RenameInvalidName_Returns400ValidationFailedAndNameUnchanged(string body)
    {
        var (f, id, _) = await Setup();
        await using var disposeF = f;

        var response = await f.PutAsync(1, $"/api/movienight/groups/{id}/name", body);

        var problem = await ApiCall.AssertProblemAsync(response, 400, "validation_failed");
        Assert.Equal("Validation failed", problem.GetProperty("title").GetString());
        Assert.Equal(NameError, problem.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("TOOLONG")]
    public async Task AC10_NonOwnerMemberRenamesWithInvalidName_Returns403BeforeValidationAndNameUnchanged(string name)
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;
        var value = name == "TOOLONG" ? new string('Я', 65) : name;

        var response = await f.PutAsync(2, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody(value));

        await MovieNightFactory.AssertGroupProblemAsync(response, 403, "movienight_not_owner", NotOwnerTitle);
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("TOOLONG")]
    public async Task AC10_NonMemberRenamesWithInvalidName_Returns404BeforeValidationAndNameUnchanged(string name)
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;
        var value = name == "TOOLONG" ? new string('Я', 65) : name;

        var response = await f.PutAsync(4, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody(value));

        await MovieNightFactory.AssertGroupProblemAsync(response, 404, "movienight_group_not_found", "Группа не найдена.");
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    [Fact]
    public async Task AC10_RenameLengthBoundary_64Ok65Fails()
    {
        var (f, id, _) = await Setup();
        await using var disposeF = f;

        var tooLong = await f.PutAsync(1, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody(new string('Я', 65)));
        var ok = await f.PutAsync(1, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody(new string('Я', 64)));

        await ApiCall.AssertProblemAsync(tooLong, 400, "validation_failed");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
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
    public async Task AC10_RenameBodyNotJsonObject_Returns400BadRequestAndNameUnchanged(string body)
    {
        var (f, id, _) = await Setup();
        await using var disposeF = f;

        var response = await f.PutAsync(1, $"/api/movienight/groups/{id}/name", body);

        var problem = await ApiCall.AssertProblemAsync(response, 400, "bad_request");
        Assert.Equal("Request body must be a JSON object.", problem.GetProperty("title").GetString());
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    // ---------------- AC-11 ----------------

    [Fact]
    public async Task AC11_OwnerRegeneratesInvite_NewCodeLinksOldRevokedAndStopsWorking()
    {
        var (f, id, oldCode) = await Setup(2);
        await using var disposeF = f;
        Assert.Equal("OLDcode123", oldCode);
        f.Codes.Enqueue("NEWcode123");

        var response = await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var invite = await ApiCall.JsonAsync(response);
        Assert.Equal("NEWcode123", invite.GetProperty("code").GetString());
        Assert.Equal("https://t.me/MovieNightBot/app?startapp=movienight_join_NEWcode123", invite.GetProperty("miniAppLink").GetString());
        Assert.Equal("https://t.me/MovieNightBot?start=movienight_join_NEWcode123", invite.GetProperty("botLink").GetString());
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes WHERE Code = 'OLDcode123'"));
        Assert.Equal("OLDcode123", f.ScalarString("SELECT Code FROM movienight_revoked_invite_codes"));
        await MovieNightFactory.AssertGroupProblemAsync(await f.GetAsync(4, "/api/movienight/invites/OLDcode123"),
            404, "movienight_invite_not_found", "Приглашение недействительно или группа больше не существует.");
        await MovieNightFactory.AssertGroupProblemAsync(await f.PostAsync(4, "/api/movienight/invites/OLDcode123/join"),
            404, "movienight_invite_not_found", "Приглашение недействительно или группа больше не существует.");
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId <> 1 AND UserId <> 2"));
        Assert.Equal(HttpStatusCode.OK, (await f.GetAsync(4, "/api/movienight/invites/NEWcode123")).StatusCode);
        Assert.Equal("NEWcode123", (await f.GetGroupAsync(2, id)).GetProperty("invite").GetProperty("code").GetString());
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC11_RegenerateTwice_BothOldCodesRevoked()
    {
        var (f, id, _) = await Setup();
        await using var disposeF = f;
        f.Codes.Enqueue("NEWcode123", "NEWcode456");

        await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");
        await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");

        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes"));
        Assert.Equal("NEWcode456", f.ScalarString("SELECT InviteCode FROM movienight_groups"));
    }

    // ---------------- AC-12 ----------------

    [Fact]
    public async Task AC12_OwnerRemovesMember_204PositionsRecalculatedAndNotificationsSent()
    {
        var (f, id, _) = await Setup(2, 3);
        await using var disposeF = f;

        var response = await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/3");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var g = await f.GetGroupAsync(1, id);
        Assert.Equal([1L, 2L], MovieNightFactory.MemberIds(g));
        Assert.Equal([1, 2], g.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("position").GetInt32()).ToArray());
        await MovieNightFactory.AssertGroupProblemAsync(await f.GetAsync(3, $"/api/movienight/groups/{id}"),
            404, "movienight_group_not_found", "Группа не найдена.");
        var list = await ApiCall.JsonAsync(await f.GetAsync(3, "/api/movienight/groups"));
        Assert.Equal(0, list.GetArrayLength());
        Assert.Equal(["Вас исключили из группы «Мы с Петей»."], f.Sender.TextsTo(3));
        Assert.Equal(["Вася больше не в группе «Мы с Петей»."], f.Sender.TextsTo(2));
        Assert.Empty(f.Sender.TextsTo(1));
        Assert.Equal(2, f.Sender.Sent.Count);
        Assert.All(f.Sender.Sent, m => Assert.False(m.Token.CanBeCanceled));
    }

    [Fact]
    public async Task AC12_RemovedMemberNotInRegistry_NobodyAddressedToHimButOthersNotified()
    {
        var f = new MovieNightFactory();
        await using var disposeF = f;
        await f.SeedAsync(1, "Аня");
        await f.SeedAsync(2, "Петя");
        var (id, _) = await f.GroupWithAsync("G", 1, 2, 3);
        f.Sender.Clear();

        var response = await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/3");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(f.Sender.TextsTo(3));
        Assert.Equal(["Участник больше не в группе «G»."], f.Sender.TextsTo(2));
    }

    // ---------------- AC-13 ----------------

    [Fact]
    public async Task AC13_OwnerRemovesSelf_Returns400CannotRemoveSelf()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var response = await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/1");

        await MovieNightFactory.AssertGroupProblemAsync(response, 400, "movienight_cannot_remove_self",
            "Чтобы уйти из группы, используйте «Выйти».");
        var ids = MovieNightFactory.MemberIds(await f.GetGroupAsync(1, id));
        Assert.Equal([1L, 2L], ids);
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC13_UserIdNotInGroup_Returns404MemberNotFound()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var response = await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/99");

        await MovieNightFactory.AssertGroupProblemAsync(response, 404, "movienight_member_not_found", "Участник не найден.");
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC13_UserIdOfMemberInAnotherGroup_Returns404MemberNotFoundAndOtherGroupIntact()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;
        var (otherId, _) = await f.GroupWithAsync("Другая", 3, 4);

        var response = await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/4");

        await MovieNightFactory.AssertGroupProblemAsync(response, 404, "movienight_member_not_found", "Участник не найден.");
        var ids = MovieNightFactory.MemberIds(await f.GetGroupAsync(3, otherId));
        Assert.Equal([3L, 4L], ids);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("99999999999999999999")]
    [InlineData("1.5")]
    public async Task AC13_UserIdNotLong_Returns404NotFoundFromRouting(string userId)
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var response = await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/{userId}");

        await ApiCall.AssertProblemAsync(response, 404, "not_found");
        var ids = MovieNightFactory.MemberIds(await f.GetGroupAsync(1, id));
        Assert.Equal([1L, 2L], ids);
    }

    // ---------------- AC-14 ----------------

    [Fact]
    public async Task AC14_RemovedMemberUsesSameLinkAgain_JoinsAsLastAndOwnerUnchanged()
    {
        var (f, id, code) = await Setup(2, 3);
        await using var disposeF = f;
        await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/2");

        var result = await f.JoinAsync(2, code);

        Assert.True(result.GetProperty("joined").GetBoolean());
        var g = await f.GetGroupAsync(2, id);
        Assert.Equal([1L, 3L, 2L], MovieNightFactory.MemberIds(g));
        Assert.Equal(1, g.GetProperty("ownerId").GetInt64());
        Assert.False(g.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task AC14_RemovedMemberAfterLinkRegeneration_CannotJoinByOldLink()
    {
        var (f, id, code) = await Setup(2);
        await using var disposeF = f;
        await f.DeleteAsync(1, $"/api/movienight/groups/{id}/members/2");
        await f.PostAsync(1, $"/api/movienight/groups/{id}/invite");

        var response = await f.PostAsync(2, $"/api/movienight/invites/{code}/join");

        await ApiCall.AssertProblemAsync(response, 404, "movienight_invite_not_found");
    }
}
