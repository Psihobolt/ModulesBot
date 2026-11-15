using System.Net;
using System.Text.Json;
using Bot.Platform.Messaging;
using Bot.Platform.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Modules.MovieNight.Tests.Helpers;

namespace Modules.MovieNight.Tests;

// TEST-5: AC-15..AC-17
public class InviteAndJoinTests
{
    private const string InviteTitle = "Приглашение недействительно или группа больше не существует.";
    private const string LimitTitle = "Можно состоять не больше чем в 20 группах.";

    // ---------------- AC-15 ----------------

    [Fact]
    public async Task AC15_PreviewByNonMember_ReturnsNamesInOrderAndAlreadyMemberFalse()
    {
        await using var f = new MovieNightFactory();
        await f.SeedDefaultsAsync(3);
        f.Codes.Enqueue("AB12cd34EF");
        await f.GroupWithAsync("Мы с Петей", 1, 2);

        var response = await f.GetAsync(3, "/api/movienight/invites/AB12cd34EF");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var p = await ApiCall.JsonAsync(response);
        Assert.Equal("Мы с Петей", p.GetProperty("groupName").GetString());
        Assert.Equal(["Аня", "Петя"], p.GetProperty("memberNames").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(2, p.GetProperty("memberCount").GetInt32());
        Assert.False(p.GetProperty("alreadyMember").GetBoolean());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("groupId").ValueKind);
    }

    [Fact]
    public async Task AC15_PreviewByMember_AlreadyMemberTrueWithGroupId()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AB12cd34EF");
        var (id, _) = await f.GroupWithAsync("G", 1, 2);

        var p = await ApiCall.JsonAsync(await f.GetAsync(2, "/api/movienight/invites/AB12cd34EF"));

        Assert.True(p.GetProperty("alreadyMember").GetBoolean());
        Assert.Equal(id, p.GetProperty("groupId").GetString());
    }

    [Fact]
    public async Task AC15_PreviewMemberWithoutRegistryRecord_ShownAsParticipant()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AB12cd34EF");
        await f.GroupWithAsync("G", 1);

        var p = await ApiCall.JsonAsync(await f.GetAsync(5, "/api/movienight/invites/AB12cd34EF"));

        Assert.Equal(["Участник"], p.GetProperty("memberNames").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public async Task AC15_PreviewDoesNotJoin()
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AB12cd34EF");
        await f.GroupWithAsync("G", 1);

        await f.GetAsync(5, "/api/movienight/invites/AB12cd34EF");

        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    public static IEnumerable<object[]> BadCodes()
    {
        yield return ["Zzzzzzzzzz"];       // формат верный, кода нет
        yield return ["ab12cd34ef"];       // другой регистр существующего AB12cd34EF
        yield return ["AB12CD34EF"];       // другой регистр
        yield return ["AB12cd34E"];        // 9 символов
        yield return ["AB12cd34EFG"];      // 11 символов
        yield return ["AB12cd34E-"];       // недопустимый символ
        yield return ["AB12cd34E_"];       // недопустимый символ
        yield return ["AB12cd34E%20"];     // пробел в конце после декодирования
        yield return ["ЖЖЖЖЖЖЖЖЖЖ"];       // не ASCII (10 символов)
    }

    [Theory]
    [MemberData(nameof(BadCodes))]
    public async Task AC15_InvalidUnknownOrDifferentCaseCode_PreviewAndJoinReturn404InviteNotFound(string code)
    {
        await using var f = new MovieNightFactory();
        f.Codes.Enqueue("AB12cd34EF");
        await f.GroupWithAsync("G", 1);

        var preview = await f.GetAsync(3, $"/api/movienight/invites/{code}");
        var join = await f.PostAsync(3, $"/api/movienight/invites/{code}/join");

        await MovieNightFactory.AssertGroupProblemAsync(preview, 404, "movienight_invite_not_found", InviteTitle);
        await MovieNightFactory.AssertGroupProblemAsync(join, 404, "movienight_invite_not_found", InviteTitle);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC15_CodeCheckPrecedesLimit_UserAtLimitWithBadCodeGets404Not409()
    {
        await using var f = new MovieNightFactory();
        for (var i = 0; i < 20; i++) await f.CreateGroupAsync(1, $"G{i}");

        var response = await f.PostAsync(1, "/api/movienight/invites/Zzzzzzzzzz/join");

        await ApiCall.AssertProblemAsync(response, 404, "movienight_invite_not_found");
    }

    // ---------------- AC-16 ----------------

    [Fact]
    public async Task AC16_NonMemberJoins_Returns200JoinedTrueAsLastOwnerUnchangedAndOthersNotified()
    {
        await using var f = new MovieNightFactory();
        await f.SeedDefaultsAsync(3);
        f.Codes.Enqueue("AB12cd34EF");
        var (id, code) = await f.GroupWithAsync("Мы с Петей", 1, 2);
        var joinedAt = TestInitData.DefaultNow.AddHours(3);
        f.Clock.UtcNow = joinedAt;
        f.Sender.Clear();

        var response = await f.PostAsync(3, $"/api/movienight/invites/{code}/join");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var r = await ApiCall.JsonAsync(response);
        Assert.True(r.GetProperty("joined").GetBoolean());
        var g = r.GetProperty("group");
        Assert.Equal(id, g.GetProperty("id").GetString());
        Assert.Equal([1L, 2L, 3L], MovieNightFactory.MemberIds(g));
        Assert.Equal(1, g.GetProperty("ownerId").GetInt64());
        Assert.False(g.GetProperty("isOwner").GetBoolean());
        var me = g.GetProperty("members")[2];
        Assert.True(me.GetProperty("isMe").GetBoolean());
        Assert.Equal(joinedAt, me.GetProperty("joinedAt").GetDateTimeOffset());
        Assert.Equal(["Вася теперь в группе «Мы с Петей»."], f.Sender.TextsTo(1));
        Assert.Equal(["Вася теперь в группе «Мы с Петей»."], f.Sender.TextsTo(2));
        Assert.Empty(f.Sender.TextsTo(3));
        Assert.Equal(2, f.Sender.Sent.Count);
        Assert.All(f.Sender.Sent, m =>
        {
            Assert.False(m.Token.CanBeCanceled);
            Assert.Null(m.Message.ReplyMarkup);
            Assert.False(m.Message.Html);
        });
    }

    [Fact]
    public async Task AC16_AlreadyMember_Returns200JoinedFalseNoChangesNoNotifications()
    {
        await using var f = new MovieNightFactory();
        await f.SeedDefaultsAsync(3);
        var (_, code) = await f.GroupWithAsync("G", 1, 2);
        f.Sender.Clear();

        var response = await f.PostAsync(2, $"/api/movienight/invites/{code}/join");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var r = await ApiCall.JsonAsync(response);
        Assert.False(r.GetProperty("joined").GetBoolean());
        Assert.Equal([1L, 2L], MovieNightFactory.MemberIds(r.GetProperty("group")));
        Assert.Empty(f.Sender.Sent);
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC16_OwnerOpensOwnInviteAndJoins_JoinedFalse()
    {
        await using var f = new MovieNightFactory();
        var (_, code) = await f.GroupWithAsync("G", 1);

        var r = await f.JoinAsync(1, code);

        Assert.False(r.GetProperty("joined").GetBoolean());
    }

    [Fact]
    public async Task AC16_RecipientRules_OnlyRegisteredReceivableMembersNotifiedExecutorExcluded()
    {
        await using var f = new MovieNightFactory();
        await f.SeedAsync(1, "Аня");                                  // получает
        // 2 — нет в реестре
        await f.SeedAsync(3, "Гоша", allowsWriteToPm: null);          // CanReceiveMessages = false
        await f.SeedAsync(4, "Дима");                                 // получает
        await f.SeedAsync(5, "Егор");                                 // исполнитель
        var (_, code) = await f.GroupWithAsync("G", 1, 2, 3, 4);
        f.Sender.Clear();

        await f.JoinAsync(5, code);

        Assert.Equal([1L, 4L], f.Sender.Recipients.OrderBy(x => x).ToArray());
        Assert.All(f.Sender.Sent, m => Assert.Equal("Егор теперь в группе «G».", m.Text));
    }

    [Fact]
    public async Task AC16_SenderReturnsBlockedFailedOrThrows_ResponseStaysOkOthersStillNotifiedWarningsLoggedWithoutText()
    {
        await using var f = new MovieNightFactory();
        await f.SeedDefaultsAsync(5);
        var (_, code) = await f.GroupWithAsync("Секретная", 1, 2, 3, 4);
        f.Sender.Behavior = id => id switch
        {
            1 => new SendResult(SendStatus.Blocked, null),
            2 => throw new InvalidOperationException("boom"),
            3 => new SendResult(SendStatus.Failed, null),
            _ => new SendResult(SendStatus.Sent, 7),
        };
        f.Sender.Clear();

        var response = await f.PostAsync(5, $"/api/movienight/invites/{code}/join");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ApiCall.JsonAsync(response)).GetProperty("joined").GetBoolean());
        Assert.Equal([1L, 2L, 3L, 4L], f.Sender.Recipients.OrderBy(x => x).ToArray());
        Assert.Contains(f.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(f.Logs.Entries, e => e.FullText.Contains("теперь в группе", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Logs.Entries, e => e.FullText.Contains(TestInitData.BotToken, StringComparison.Ordinal));
        Assert.Equal(5, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC16_NotificationsSentAfterCommit_StateAlreadyVisibleInDbDuringSend()
    {
        await using var f = new MovieNightFactory();
        await f.SeedDefaultsAsync(3);
        var (_, code) = await f.GroupWithAsync("G", 1, 2);
        long membersSeenDuringSend = -1;
        f.Sender.Behavior = _ =>
        {
            membersSeenDuringSend = f.Scalar("SELECT COUNT(*) FROM movienight_group_members");
            return new SendResult(SendStatus.Sent, 1);
        };

        await f.JoinAsync(3, code);

        Assert.Equal(3, membersSeenDuringSend);
    }

    [Fact]
    public async Task AC16_JoinAtLimitIsCheckedAfterAlreadyMember_Order_CodeThenMemberThenLimit()
    {
        await using var f = new MovieNightFactory();
        var other = await f.CreateGroupAsync(2, "Чужая");
        var code = other.GetProperty("invite").GetProperty("code").GetString()!;
        for (var i = 0; i < 20; i++) await f.CreateGroupAsync(1, $"G{i}");

        var response = await f.PostAsync(1, $"/api/movienight/invites/{code}/join");

        await MovieNightFactory.AssertGroupProblemAsync(response, 409, "movienight_group_limit", LimitTitle);
    }

    // ---------------- AC-17 ----------------

    [Fact]
    public async Task AC17_SameUserJoinsTwiceConcurrently_BothOkOneJoinedTrueOneMemberRowOneNotification()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var f = new MovieNightFactory();
            await f.SeedDefaultsAsync(2);
            var (_, code) = await f.GroupWithAsync("G", 1);
            f.Sender.Clear();

            var responses = await Task.WhenAll(
                f.PostAsync(2, $"/api/movienight/invites/{code}/join"),
                f.PostAsync(2, $"/api/movienight/invites/{code}/join"));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var joined = new List<bool>();
            foreach (var r in responses) joined.Add((await ApiCall.JsonAsync(r)).GetProperty("joined").GetBoolean());
            Assert.Equal([false, true], joined.OrderBy(x => x).ToArray());
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 2"));
            Assert.Single(f.Sender.Sent);
            Assert.Equal(1, f.Sender.Sent[0].ChatId);
        }
    }

    [Fact]
    public async Task AC17_TwoDifferentUsersJoinConcurrently_BothInGroupWithDistinctPositions()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var f = new MovieNightFactory();
            await f.SeedDefaultsAsync(3);
            var (id, code) = await f.GroupWithAsync("G", 1);

            var responses = await Task.WhenAll(
                f.PostAsync(2, $"/api/movienight/invites/{code}/join"),
                f.PostAsync(3, $"/api/movienight/invites/{code}/join"));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var g = await f.GetGroupAsync(1, id);
            var members = g.GetProperty("members").EnumerateArray().ToArray();
            Assert.Equal(3, members.Length);
            Assert.Equal([1, 2, 3], members.Select(m => m.GetProperty("position").GetInt32()).ToArray());
            Assert.Equal([1L, 2L, 3L], MovieNightFactory.MemberIds(g).OrderBy(x => x).ToArray());
            Assert.Equal(3, f.Scalar("SELECT COUNT(DISTINCT JoinSeq) FROM movienight_group_members"));
        }
    }

    [Fact]
    public async Task AC17_UserWith19GroupsJoinsTwoNewConcurrently_ExactlyOneSucceedsOtherGets409()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var f = new MovieNightFactory();
            for (var i = 0; i < 19; i++) await f.CreateGroupAsync(1, $"G{i}");
            var a = (await f.CreateGroupAsync(2, "A")).GetProperty("invite").GetProperty("code").GetString()!;
            var b = (await f.CreateGroupAsync(3, "B")).GetProperty("invite").GetProperty("code").GetString()!;

            var responses = await Task.WhenAll(
                f.PostAsync(1, $"/api/movienight/invites/{a}/join"),
                f.PostAsync(1, $"/api/movienight/invites/{b}/join"));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).OrderBy(x => (int)x).ToArray());
            var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            await ApiCall.AssertProblemAsync(conflict, 409, "movienight_group_limit");
            Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 1"));
        }
    }
}
