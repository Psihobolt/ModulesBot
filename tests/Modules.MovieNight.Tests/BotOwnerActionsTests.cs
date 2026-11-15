using Microsoft.Extensions.DependencyInjection;
using Modules.MovieNight.Application;
using Modules.MovieNight.Tests.Helpers;
using static Modules.MovieNight.Tests.Helpers.BotDriver;

namespace Modules.MovieNight.Tests;

// TEST-4 (005): AC-12, AC-13, AC-13a + RemoveMembershipAsync (BE-1)
public class BotOwnerActionsTests
{
    private const string Name = "Мы с Петей";
    private const string OldCode = "OLDcode123";
    private const string NewCode = "NEWcode123";

    private static async Task<(MovieNightFactory F, string Id)> Setup(long[]? members = null, string? username = "MovieNightBot")
    {
        var f = await NewAsync(4, "https://example.test/", username);
        f.Codes.Enqueue(OldCode);
        var (id, _) = await f.GroupWithAsync(Name, 1, members ?? []);
        f.Sender.Clear();
        return (f, id);
    }

    private static long Seq(MovieNightFactory f, long userId) =>
        f.Scalar($"SELECT JoinSeq FROM movienight_group_members WHERE UserId = {userId} ORDER BY JoinSeq DESC LIMIT 1");

    private static async Task AssertMembers(MovieNightFactory f, string id, long[] expected)
    {
        var actual = MovieNightFactory.MemberIds(await f.GetGroupAsync(1, id));
        Assert.Equal(expected, actual);
    }

    // ---------------- AC-12 ----------------

    [Fact]
    public async Task AC12_OwnerPressesNewLink_EditsConfirmAndCodeStaysSame()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:newlink:{id}", messageId: 12);

        Assert.Empty(r.Sends);
        Assert.Equal(12, r.Edit.MessageId);
        Assert.Equal("Старая ссылка перестанет работать. Создать новую?", r.Edit.Message.Text);
        Assert.Equal([[$"Да, новая ссылка|movienight:newlinkok:{id}"], [$"Отмена|movienight:g:{id}"]], Rows(r.Edit.Message));
        Assert.Equal(OldCode, f.ScalarString("SELECT InviteCode FROM movienight_groups"));
    }

    [Fact]
    public async Task AC12_OwnerConfirmsNewLink_CardEditedAnswerAndNewInviteMessageWithNewLinks()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        f.Codes.Enqueue(NewCode);

        var r = await f.Click(1, $"movienight:newlinkok:{id}", messageId: 12);

        Assert.Equal(NewCode, f.ScalarString("SELECT InviteCode FROM movienight_groups"));
        Assert.Equal(12, r.Edit.MessageId);
        Assert.Equal($"Группа «{Name}»\nУчастники (по порядку вступления):\n1. Аня (вы) — владелец\n2. Петя", r.Edit.Message.Text);
        Assert.Contains($"Участники|movienight:members:{id}", Buttons(r.Edit.Message));
        Assert.Equal("Создана новая ссылка", r.AnswerText);
        Assert.False(r.AnswerAlert);
        var invite = r.SendTo(1);
        Assert.Equal(InviteText(Name, NewCode), invite.Message.Text);
        AssertNoKeyboard(invite.Message);
        Assert.Single(r.Sends);
        Assert.Empty(r.SendsTo(2));
    }

    [Fact]
    public async Task AC12_AfterNewLink_OldCodeInStartIsInvalidAndNewCodeWorks()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        f.Codes.Enqueue(NewCode);
        await f.Click(1, $"movienight:newlinkok:{id}");
        f.Sender.Clear();

        var old = await f.Start(4, "movienight_join_" + OldCode);
        var fresh = await f.Start(4, "movienight_join_" + NewCode);

        Assert.Equal(InviteInvalid, old.SendTo(4).Message.Text);
        AssertNoKeyboard(old.SendTo(4).Message);
        Assert.StartsWith($"Приглашение в группу «{Name}»", fresh.SendTo(4).Message.Text);
    }

    [Fact]
    public async Task AC12_UsernameNotSet_NewLinkStillIssuedCardAnswerAndNoInviteMessage()
    {
        var (f, id) = await Setup(username: null);
        await using var _ = f;
        f.Codes.Enqueue(NewCode);

        var r = await f.Click(1, $"movienight:newlinkok:{id}");

        Assert.Equal(NewCode, f.ScalarString("SELECT InviteCode FROM movienight_groups"));
        Assert.Equal("Создана новая ссылка", r.AnswerText);
        Assert.Contains("Группа «" + Name + "»", r.Edit.Message.Text);
        Assert.Empty(r.Sends);
    }

    [Fact]
    public async Task AC12_NonOwnerAndNonMember_GetNotOwnerAndNotMemberAndCodeNotChanged()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var member = await f.Click(2, $"movienight:newlinkok:{id}");
        var stranger = await f.Click(4, $"movienight:newlinkok:{id}");

        Assert.Equal(NotOwner, member.AnswerText);
        Assert.Equal(NotInGroup, stranger.AnswerText);
        Assert.Equal(OldCode, f.ScalarString("SELECT InviteCode FROM movienight_groups"));
        Assert.Empty(member.Sends.Concat(stranger.Sends));
    }

    [Fact]
    public async Task AC12_DoubleNewLinkOk_BothSucceedAndOnlyLastCodeIsValid()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        f.Codes.Enqueue("NEWcode001", "NEWcode002");

        await f.Click(1, $"movienight:newlinkok:{id}");
        await f.Click(1, $"movienight:newlinkok:{id}");

        Assert.Equal("NEWcode002", f.ScalarString("SELECT InviteCode FROM movienight_groups"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes"));
    }

    // ---------------- AC-13 ----------------

    [Fact]
    public async Task AC13_OwnerPressesMembers_EditsListOfOthersWithKickButtonsByJoinSeq()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:members:{id}", messageId: 31);

        Assert.Empty(r.Sends);
        Assert.Equal(31, r.Edit.MessageId);
        Assert.Equal($"Участники группы «{Name}»:", r.Edit.Message.Text);
        Assert.Equal(
            [
                [$"Исключить Петя|movienight:kick:{id}:{Seq(f, 2)}"],
                [$"Исключить Вася|movienight:kick:{id}:{Seq(f, 3)}"],
                [$"« К группе|movienight:g:{id}"],
            ],
            Rows(r.Edit.Message));
    }

    [Fact]
    public async Task AC13_OwnerAloneInGroup_MembersListHasOnlyBackButton()
    {
        var (f, id) = await Setup();
        await using var _ = f;

        var r = await f.Click(1, $"movienight:members:{id}");

        Assert.Equal([[$"« К группе|movienight:g:{id}"]], Rows(r.Edit.Message));
    }

    [Fact]
    public async Task AC13_OwnerPressesKick_EditsConfirmWithoutChanges()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;
        var seq = Seq(f, 2);

        var r = await f.Click(1, $"movienight:kick:{id}:{seq}");

        Assert.Equal($"Исключить Петя из группы «{Name}»?", r.Edit.Message.Text);
        Assert.Equal([[$"Да, исключить|movienight:kickok:{id}:{seq}"], [$"Отмена|movienight:members:{id}"]], Rows(r.Edit.Message));
        await AssertMembers(f, id, [1L, 2L, 3L]);
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC13_OwnerConfirmsKick_MemberRemovedListEditedAnswerAndNotificationsToVictimAndOthersNotExecutor()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:kickok:{id}:{Seq(f, 2)}", messageId: 31);

        await AssertMembers(f, id, [1L, 3L]);
        Assert.Equal(31, r.Edit.MessageId);
        Assert.Equal($"Участники группы «{Name}»:", r.Edit.Message.Text);
        Assert.Equal([[$"Исключить Вася|movienight:kick:{id}:{Seq(f, 3)}"], [$"« К группе|movienight:g:{id}"]], Rows(r.Edit.Message));
        Assert.Equal("Петя исключён(а) из группы", r.AnswerText);
        Assert.False(r.AnswerAlert);
        Assert.Equal([$"Вас исключили из группы «{Name}»."], f.Sender.TextsTo(2));
        Assert.Equal([$"Петя больше не в группе «{Name}»."], f.Sender.TextsTo(3));
        Assert.Empty(f.Sender.TextsTo(1));
    }

    [Fact]
    public async Task AC13_LastOtherMemberKicked_ListShowsOnlyBackButton()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:kickok:{id}:{Seq(f, 2)}");

        Assert.Equal([[$"« К группе|movienight:g:{id}"]], Rows(r.Edit.Message));
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("kickok")]
    public async Task AC13_MemberLeftAndRejoined_OldJoinSeqIsStaleAndNewMembershipStays(string action)
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;
        var code = (await f.GetGroupAsync(1, id)).GetProperty("invite").GetProperty("code").GetString()!;
        var oldSeq = Seq(f, 2);
        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");
        await f.JoinAsync(2, code);
        f.Sender.Clear();
        Assert.NotEqual(oldSeq, Seq(f, 2));

        var r = await f.Click(1, $"movienight:{action}:{id}:{oldSeq}");

        Assert.Equal(MemberGone, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Equal($"Участники группы «{Name}»:", r.Edit.Message.Text);
        Assert.Contains($"Исключить Петя|movienight:kick:{id}:{Seq(f, 2)}", Buttons(r.Edit.Message));
        await AssertMembers(f, id, [1L, 3L, 2L]);
        Assert.Empty(f.Sender.Sent);
    }

    [Theory]
    [InlineData("kick", "abc")]
    [InlineData("kickok", "abc")]
    [InlineData("kick", "99999999999999999999")]
    [InlineData("kickok", "99999999999999999999")]
    [InlineData("kickok", "-2")]
    [InlineData("kickok", "+2")]
    [InlineData("kickok", "1.5")]
    [InlineData("kickok", "0x2")]
    [InlineData("kick", "")]
    [InlineData("kickok", "")]
    [InlineData("kickok", "999999")]
    public async Task AC13_JoinSeqUnparsableOrNonexistent_MemberGoneAlertListEditedNobodyRemoved(string action, string seq)
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:{action}:{id}:{seq}");

        Assert.Equal(MemberGone, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Equal($"Участники группы «{Name}»:", r.Edit.Message.Text);
        await AssertMembers(f, id, [1L, 2L, 3L]);
        Assert.Empty(f.Sender.Sent);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("kickok")]
    public async Task AC13_JoinSeqOfMemberOfAnotherGroup_MemberGoneAndThatMemberStays(string action)
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        await f.GroupWithAsync("Другая", 4, 3);
        var foreign = Seq(f, 3);
        f.Sender.Clear();

        var r = await f.Click(1, $"movienight:{action}:{id}:{foreign}");

        Assert.Equal(MemberGone, r.AnswerText);
        Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 3"));
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC13_OwnJoinSeqOnKick_TreatedAsMemberGone()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:kick:{id}:{Seq(f, 1)}");

        Assert.Equal(MemberGone, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Equal($"Участники группы «{Name}»:", r.Edit.Message.Text);
    }

    [Fact]
    public async Task AC13_OwnJoinSeqOnKickOk_CannotRemoveSelfAlertAndMembersListNothingChanged()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:kickok:{id}:{Seq(f, 1)}");

        Assert.Equal("Чтобы уйти из группы, используйте «Выйти».", r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Equal($"Участники группы «{Name}»:", r.Edit.Message.Text);
        await AssertMembers(f, id, [1L, 2L]);
        Assert.Empty(f.Sender.Sent);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("kick")]
    [InlineData("kickok")]
    public async Task AC13_NonOwnerAndNonMember_NotOwnerAndNotMemberRespectively(string action)
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;
        var tail = action == "members" ? "" : ":" + Seq(f, 3);

        var member = await f.Click(2, $"movienight:{action}:{id}{tail}");
        var stranger = await f.Click(4, $"movienight:{action}:{id}{tail}");

        Assert.Equal(NotOwner, member.AnswerText);
        Assert.Equal(NotInGroup, stranger.AnswerText);
        await AssertMembers(f, id, [1L, 2L, 3L]);
        Assert.Empty(f.Sender.Sent);
    }

    // ---------------- AC-13a ----------------

    [Fact]
    public async Task AC13a_DoubleKickOkInRow_FirstKicksSecondMemberGoneNotificationsOnce()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;
        var data = $"movienight:kickok:{id}:{Seq(f, 2)}";

        var first = await f.Click(1, data);
        var second = await f.Click(1, data);

        Assert.Equal("Петя исключён(а) из группы", first.AnswerText);
        Assert.Equal(MemberGone, second.AnswerText);
        Assert.True(second.AnswerAlert);
        Assert.Equal($"Участники группы «{Name}»:", second.Edit.Message.Text);
        Assert.Single(f.Sender.TextsTo(2));
        Assert.Single(f.Sender.TextsTo(3));
        await AssertMembers(f, id, [1L, 3L]);
    }

    [Fact]
    public async Task AC13a_ConcurrentKickOk_OneKicksOneMemberGoneNotificationsOnce()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;
        var data = $"movienight:kickok:{id}:{Seq(f, 2)}";

        var results = await Task.WhenAll(f.Click(1, data, 1), f.Click(1, data, 2));

        Assert.Single(results, r => r.AnswerText == "Петя исключён(а) из группы");
        Assert.Single(results, r => r.AnswerText == MemberGone);
        Assert.Single(f.Sender.TextsTo(2));
        Assert.Single(f.Sender.TextsTo(3));
    }

    // ---------------- BE-1: RemoveMembershipAsync ----------------

    private static async Task<T> InScope<T>(MovieNightFactory f, Func<IGroupService, Task<T>> action)
    {
        using var scope = f.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IGroupService>());
    }

    private static async Task<GroupError> ErrorOf(MovieNightFactory f, long user, string id, long seq)
    {
        var ex = await Assert.ThrowsAsync<GroupException>(() =>
            InScope(f, s => s.RemoveMembershipAsync(user, Guid.Parse(id), seq, CancellationToken.None)));
        return ex.Error;
    }

    [Fact]
    public async Task BE1_Owner_RemovesMemberByJoinSeqReturnsDisplayNameAndNotifies()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;

        var name = await InScope(f, s => s.RemoveMembershipAsync(1, Guid.Parse(id), Seq(f, 2), CancellationToken.None));

        Assert.Equal("Петя", name);
        await AssertMembers(f, id, [1L, 3L]);
        Assert.Equal([$"Вас исключили из группы «{Name}»."], f.Sender.TextsTo(2));
        Assert.Equal([$"Петя больше не в группе «{Name}»."], f.Sender.TextsTo(3));
        Assert.Empty(f.Sender.TextsTo(1));
    }

    [Fact]
    public async Task BE1_CallerNotMember_GroupNotFoundBeforeOtherChecks()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        Assert.Equal(GroupError.GroupNotFound, await ErrorOf(f, 4, id, Seq(f, 2)));
        Assert.Equal(GroupError.GroupNotFound, await ErrorOf(f, 4, id, 999));
        Assert.Equal(GroupError.GroupNotFound, await ErrorOf(f, 1, Guid.NewGuid().ToString(), Seq(f, 2)));
    }

    [Fact]
    public async Task BE1_CallerNotOwner_NotOwnerBeforeMemberNotFoundAndSelfChecks()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;

        Assert.Equal(GroupError.NotOwner, await ErrorOf(f, 2, id, Seq(f, 3)));
        Assert.Equal(GroupError.NotOwner, await ErrorOf(f, 2, id, 999));
        Assert.Equal(GroupError.NotOwner, await ErrorOf(f, 2, id, Seq(f, 2)));
        await AssertMembers(f, id, [1L, 2L, 3L]);
    }

    [Fact]
    public async Task BE1_OwnerWithUnknownOrForeignOrNegativeJoinSeq_MemberNotFound()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        await f.GroupWithAsync("Другая", 4, 3);
        f.Sender.Clear();

        Assert.Equal(GroupError.MemberNotFound, await ErrorOf(f, 1, id, 999999));
        Assert.Equal(GroupError.MemberNotFound, await ErrorOf(f, 1, id, 0));
        Assert.Equal(GroupError.MemberNotFound, await ErrorOf(f, 1, id, -1));
        Assert.Equal(GroupError.MemberNotFound, await ErrorOf(f, 1, id, Seq(f, 3)));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_group_members WHERE UserId = 3"));
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task BE1_OwnersOwnJoinSeq_CannotRemoveSelf()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        Assert.Equal(GroupError.CannotRemoveSelf, await ErrorOf(f, 1, id, Seq(f, 1)));
        await AssertMembers(f, id, [1L, 2L]);
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task BE1_SameJoinSeqTwice_SecondIsMemberNotFound()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;
        var seq = Seq(f, 2);

        await InScope(f, s => s.RemoveMembershipAsync(1, Guid.Parse(id), seq, CancellationToken.None));

        Assert.Equal(GroupError.MemberNotFound, await ErrorOf(f, 1, id, seq));
        Assert.Single(f.Sender.TextsTo(2));
    }

    [Fact]
    public async Task BE1_RemoveMemberAsyncByUserId_StillWorksUnchanged()
    {
        var (f, id) = await Setup([2, 3]);
        await using var _ = f;

        await InScope(f, async s =>
        {
            await s.RemoveMemberAsync(1, Guid.Parse(id), 2, CancellationToken.None);
            return 0;
        });

        await AssertMembers(f, id, [1L, 3L]);
    }
}
