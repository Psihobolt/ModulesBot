using System.Net;
using System.Text.Json;
using Bot.Platform.Tests.Helpers;
using Modules.MovieNight.Tests.Helpers;

namespace Modules.MovieNight.Tests;

// TEST-5: AC-18..AC-22
public class LeaveGroupTests
{
    private static async Task<(MovieNightFactory F, string Id, string Code)> Setup(params long[] members)
    {
        var f = new MovieNightFactory();
        await f.SeedDefaultsAsync(4);
        f.Codes.Enqueue("AB12cd34EF");
        var (id, code) = await f.GroupWithAsync("Мы с Петей", 1, members);
        f.Sender.Clear();
        return (f, id, code);
    }

    private static void AssertOwnerInvariant(JsonElement g)
    {
        var first = g.GetProperty("members")[0];
        Assert.Equal(first.GetProperty("userId").GetInt64(), g.GetProperty("ownerId").GetInt64());
        Assert.True(first.GetProperty("isOwner").GetBoolean());
        Assert.Equal(1, g.GetProperty("members").EnumerateArray().Count(m => m.GetProperty("isOwner").GetBoolean()));
    }

    // ---------------- AC-18 ----------------

    [Fact]
    public async Task AC18_MemberLeaves_204MembersUpdatedGroupGoneOthersNotified()
    {
        var (f, id, _) = await Setup(2, 3);
        await using var disposeF = f;

        var response = await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var g = await f.GetGroupAsync(1, id);
        Assert.Equal([1L, 3L], MovieNightFactory.MemberIds(g));
        Assert.Equal(1, g.GetProperty("ownerId").GetInt64());
        Assert.Equal(0, (await ApiCall.JsonAsync(await f.GetAsync(2, "/api/movienight/groups"))).GetArrayLength());
        Assert.Equal(["Петя больше не в группе «Мы с Петей»."], f.Sender.TextsTo(1));
        Assert.Equal(["Петя больше не в группе «Мы с Петей»."], f.Sender.TextsTo(3));
        Assert.Empty(f.Sender.TextsTo(2));
        Assert.Equal(2, f.Sender.Sent.Count);
        Assert.All(f.Sender.Sent, m => Assert.False(m.Token.CanBeCanceled));
    }

    [Fact]
    public async Task AC18_LeaveRecipientsFollowRegistryRules_NotInRegistryOrBlockedFlagSkipped()
    {
        var f = new MovieNightFactory();
        await using var disposeF = f;
        await f.SeedAsync(1, "Аня");
        await f.SeedAsync(2, "Петя");
        await f.SeedAsync(3, "Вася", allowsWriteToPm: null);   // CanReceiveMessages = false
        var (id, _) = await f.GroupWithAsync("G", 1, 2, 3, 4);  // 4 — нет в реестре
        f.Sender.Clear();

        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");

        Assert.Equal([1L], f.Sender.Recipients.ToArray());
    }

    // ---------------- AC-19 ----------------

    [Fact]
    public async Task AC19_OwnerLeaves_NextByJoinOrderBecomesOwnerAndGetsSpecialText()
    {
        var (f, id, code) = await Setup(2, 3);
        await using var disposeF = f;

        var response = await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var g = await f.GetGroupAsync(2, id);
        Assert.Equal(2, g.GetProperty("ownerId").GetInt64());
        Assert.True(g.GetProperty("isOwner").GetBoolean());
        Assert.Equal([2L, 3L], MovieNightFactory.MemberIds(g));
        AssertOwnerInvariant(g);
        Assert.Equal(code, g.GetProperty("invite").GetProperty("code").GetString());
        Assert.Equal(["Аня больше не в группе «Мы с Петей». Теперь вы владелец группы."], f.Sender.TextsTo(2));
        Assert.Equal(["Аня больше не в группе «Мы с Петей»."], f.Sender.TextsTo(3));
        Assert.Empty(f.Sender.TextsTo(1));
        Assert.Equal(2, f.Sender.Sent.Count);
        Assert.Equal(2, f.Scalar("SELECT OwnerId FROM movienight_groups"));
    }

    [Fact]
    public async Task AC19_NewOwnerCanNowRunOwnerActions_OldOwnerCannotSeeGroup()
    {
        var (f, id, _) = await Setup(2, 3);
        await using var disposeF = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");

        var rename = await f.PutAsync(2, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody("Новое"));
        var oldOwner = await f.PutAsync(1, $"/api/movienight/groups/{id}/name", MovieNightFactory.NameBody("Старое"));

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        await ApiCall.AssertProblemAsync(oldOwner, 404, "movienight_group_not_found");
    }

    [Fact]
    public async Task AC19_NonOwnerLeaves_OwnerDoesNotChange()
    {
        var (f, id, _) = await Setup(2, 3);
        await using var disposeF = f;

        await f.PostAsync(3, $"/api/movienight/groups/{id}/leave");

        Assert.Equal(1, (await f.GetGroupAsync(1, id)).GetProperty("ownerId").GetInt64());
        Assert.DoesNotContain(f.Sender.Sent, m => m.Text.Contains("владелец", StringComparison.Ordinal));
    }

    // ---------------- AC-20 ----------------

    [Fact]
    public async Task AC20_OwnerAndNextLeaveConcurrently_RemainingMemberWithLowestOrderIsOwner()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (f, id, _) = await Setup(2, 3);
            await using var disposeF = f;

            var responses = await Task.WhenAll(
                f.PostAsync(1, $"/api/movienight/groups/{id}/leave"),
                f.PostAsync(2, $"/api/movienight/groups/{id}/leave"));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
            var g = await f.GetGroupAsync(3, id);
            Assert.Equal([3L], MovieNightFactory.MemberIds(g));
            Assert.Equal(3, g.GetProperty("ownerId").GetInt64());
            AssertOwnerInvariant(g);
        }
    }

    [Fact]
    public async Task AC20_OwnerAndLastLeaveConcurrently_SecondBecomesOwner()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (f, id, _) = await Setup(2, 3);
            await using var disposeF = f;

            var responses = await Task.WhenAll(
                f.PostAsync(1, $"/api/movienight/groups/{id}/leave"),
                f.PostAsync(3, $"/api/movienight/groups/{id}/leave"));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
            var g = await f.GetGroupAsync(2, id);
            Assert.Equal([2L], MovieNightFactory.MemberIds(g));
            AssertOwnerInvariant(g);
        }
    }

    [Fact]
    public async Task AC20_OwnerInvariantHoldsAfterSequenceOfJoinsLeavesAndRemovals()
    {
        var (f, id, code) = await Setup(2, 3, 4);
        await using var disposeF = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");      // owner -> 2
        await f.JoinAsync(1, code);                                        // [2,3,4,1]
        AssertOwnerInvariant(await f.GetGroupAsync(2, id));
        await f.DeleteAsync(2, $"/api/movienight/groups/{id}/members/3");  // [2,4,1]
        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");        // owner -> 4

        var g = await f.GetGroupAsync(4, id);

        Assert.Equal([4L, 1L], MovieNightFactory.MemberIds(g));
        Assert.Equal(4, g.GetProperty("ownerId").GetInt64());
        AssertOwnerInvariant(g);
    }

    // ---------------- AC-21 ----------------

    [Fact]
    public async Task AC21_MemberLeavesAndRejoins_BecomesLastAndNotOwner()
    {
        var (f, id, code) = await Setup(2, 3);
        await using var disposeF = f;
        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");

        await f.JoinAsync(2, code);

        var g = await f.GetGroupAsync(2, id);
        Assert.Equal([1L, 3L, 2L], MovieNightFactory.MemberIds(g));
        Assert.Equal(1, g.GetProperty("ownerId").GetInt64());
        Assert.False(g.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task AC21_FormerOwnerRejoins_BecomesLastNotOwner()
    {
        var (f, id, code) = await Setup(2, 3);
        await using var disposeF = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");

        await f.JoinAsync(1, code);

        var g = await f.GetGroupAsync(1, id);
        Assert.Equal([2L, 3L, 1L], MovieNightFactory.MemberIds(g));
        Assert.Equal(2, g.GetProperty("ownerId").GetInt64());
        Assert.False(g.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task AC21_RejoinWithSameFakeClockTime_StillLastBecauseOrderIsByJoinSeq()
    {
        var (f, id, code) = await Setup(2, 3);
        await using var disposeF = f;
        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");
        await f.JoinAsync(2, code);
        await f.PostAsync(3, $"/api/movienight/groups/{id}/leave");
        await f.JoinAsync(3, code);

        var ids = MovieNightFactory.MemberIds(await f.GetGroupAsync(1, id));
        Assert.Equal([1L, 2L, 3L], ids);
    }

    // ---------------- AC-22 ----------------

    [Fact]
    public async Task AC22_SoleOwnerLeaves_204GroupKeptWithoutOwnerInviteDeadNoNotifications()
    {
        var (f, id, code) = await Setup();
        await using var disposeF = f;

        var response = await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups WHERE OwnerId IS NULL"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
        await MovieNightFactory.AssertGroupProblemAsync(await f.GetAsync(2, $"/api/movienight/invites/{code}"),
            404, "movienight_invite_not_found", "Приглашение недействительно или группа больше не существует.");
        await ApiCall.AssertProblemAsync(await f.PostAsync(2, $"/api/movienight/invites/{code}/join"), 404, "movienight_invite_not_found");
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC22_RepeatedLeave_Returns404GroupNotFound()
    {
        var (f, id, _) = await Setup();
        await using var disposeF = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");

        var again = await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");

        await MovieNightFactory.AssertGroupProblemAsync(again, 404, "movienight_group_not_found", "Группа не найдена.");
    }

    [Fact]
    public async Task AC22_RepeatedLeaveOfRegularMember_Returns404AndNoDuplicateNotifications()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;
        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");
        f.Sender.Clear();

        var again = await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");

        await ApiCall.AssertProblemAsync(again, 404, "movienight_group_not_found");
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC22_SameMemberLeavesTwiceConcurrently_OneNoContentOtherNotFoundOneNotification()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;

        var responses = await Task.WhenAll(
            f.PostAsync(2, $"/api/movienight/groups/{id}/leave"),
            f.PostAsync(2, $"/api/movienight/groups/{id}/leave"));

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound],
            responses.Select(r => r.StatusCode).OrderBy(x => (int)x).ToArray());
        Assert.Single(f.Sender.Sent);
    }

    [Fact]
    public async Task AC22_UserInTwoGroupsLeavesOne_OtherGroupAndListIntact()
    {
        var (f, id, _) = await Setup(2);
        await using var disposeF = f;
        var other = await f.CreateGroupAsync(2, "Вторая");

        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");

        var list = (await ApiCall.JsonAsync(await f.GetAsync(2, "/api/movienight/groups"))).EnumerateArray().ToArray();
        var item = Assert.Single(list);
        Assert.Equal(other.GetProperty("id").GetString(), item.GetProperty("id").GetString());
    }
}
