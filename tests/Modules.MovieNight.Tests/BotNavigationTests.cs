using Microsoft.Extensions.Logging;
using Modules.MovieNight.Tests.Helpers;
using Telegram.Bot.Types.ReplyMarkups;
using static Modules.MovieNight.Tests.Helpers.BotDriver;

namespace Modules.MovieNight.Tests;

// TEST-3 (005): AC-2, AC-6..AC-10, AC-14
public class BotNavigationTests
{
    private const string Name = "Мы с Петей";

    private static async Task<(MovieNightFactory F, string Id, string Code)> Setup(
        int users = 4, string? publicUrl = "https://example.test/", string? username = "MovieNightBot", long[]? members = null)
    {
        var f = await NewAsync(users, publicUrl, username);
        var (id, code) = await f.GroupWithAsync(Name, 1, members ?? []);
        f.Sender.Clear();
        return (f, id, code);
    }

    private static string[][] OwnerRows(string id, bool multi, bool web = true)
    {
        var rows = new List<string[]>
        {
            new[] { $"Пригласить|movienight:invite:{id}" },
            new[] { $"Переименовать|movienight:rename:{id}" },
            new[] { $"Новая ссылка|movienight:newlink:{id}" },
        };
        if (multi) rows.Add([$"Участники|movienight:members:{id}"]);
        rows.Add([$"Выйти из группы|movienight:leave:{id}"]);
        rows.Add(["« К списку групп|movienight:groups"]);
        if (web) rows.Add([WebBtn]);
        return rows.ToArray();
    }

    private static string[][] MemberRows(string id, bool web = true)
    {
        var rows = new List<string[]>
        {
            new[] { $"Пригласить|movienight:invite:{id}" },
            new[] { $"Выйти из группы|movienight:leave:{id}" },
            new[] { "« К списку групп|movienight:groups" },
        };
        if (web) rows.Add([WebBtn]);
        return rows.ToArray();
    }

    // ---------------- AC-2 ----------------

    [Fact]
    public async Task AC2_GroupsCommand_SendsNewListNewestJoinedFirstWithStarForOwnedAndCreateButton()
    {
        await using var f = await NewAsync();
        var (idA, _) = await f.GroupWithAsync("Альфа", 1, 3);
        var (idB, _) = await f.GroupWithAsync("Бета", 2, 1);
        f.Sender.Clear();

        var r = await f.Cmd(1, "groups");

        Assert.Empty(r.Edits);
        var msg = r.SendTo(1).Message;
        Assert.Equal("Ваши группы:", msg.Text);
        Assert.False(msg.Html);
        Assert.Equal(
            [[$"Бета · 2|movienight:g:{idB}"], [$"★ Альфа · 2|movienight:g:{idA}"], [NewGroupBtn]],
            Rows(msg));
    }

    [Fact]
    public async Task AC2_NoGroups_SendsEmptyTextWithOnlyCreateButton()
    {
        await using var f = await NewAsync();

        var r = await f.Cmd(1, "groups");

        var msg = r.SendTo(1).Message;
        Assert.Equal("У вас пока нет групп. Создайте группу и пригласите друзей.", msg.Text);
        Assert.Equal([[NewGroupBtn]], Rows(msg));
    }

    [Fact]
    public async Task AC2_GroupsOfOtherUser_AreNotShown()
    {
        await using var f = await NewAsync();
        await f.GroupWithAsync("Чужая", 2, 3);
        f.Sender.Clear();

        var r = await f.Cmd(1, "groups");

        Assert.Equal([[NewGroupBtn]], Rows(r.SendTo(1).Message));
    }

    // ---------------- AC-6 ----------------

    [Fact]
    public async Task AC6_OwnerOpensCardOfThreeMembers_EditsMessageWithOwnerKeyboard()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:g:{id}", messageId: 321);

        Assert.Empty(r.Sends);
        var edit = r.Edit;
        Assert.Equal((1L, 321), (edit.ChatId, edit.MessageId!.Value));
        Assert.Equal($"Группа «{Name}»\nУчастники (по порядку вступления):\n1. Аня (вы) — владелец\n2. Петя\n3. Вася", edit.Message.Text);
        Assert.Equal(OwnerRows(id, multi: true), Rows(edit.Message));
        Assert.Null(r.AnswerText);
    }

    [Fact]
    public async Task AC6_MemberOpensCard_EditsWithMemberKeyboardAndOwnMarkOnHimself()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(3, $"movienight:g:{id}");

        var edit = r.Edit;
        Assert.Equal($"Группа «{Name}»\nУчастники (по порядку вступления):\n1. Аня — владелец\n2. Петя\n3. Вася (вы)", edit.Message.Text);
        Assert.Equal(MemberRows(id), Rows(edit.Message));
    }

    [Fact]
    public async Task AC6_OwnerAlone_NoMembersButton()
    {
        var (f, id, _) = await Setup();
        await using var _ = f;

        var r = await f.Click(1, $"movienight:g:{id}");

        Assert.Equal(OwnerRows(id, multi: false), Rows(r.Edit.Message));
    }

    [Fact]
    public async Task AC6_PublicUrlNotSet_NoOpenAppRow()
    {
        var (f, id, _) = await Setup(publicUrl: null, members: [2]);
        await using var _ = f;

        var owner = await f.Click(1, $"movienight:g:{id}");
        var member = await f.Click(2, $"movienight:g:{id}");

        Assert.Equal(OwnerRows(id, multi: true, web: false), Rows(owner.Edit.Message));
        Assert.Equal(MemberRows(id, web: false), Rows(member.Edit.Message));
    }

    [Fact]
    public async Task AC6_OpenAppButton_IsWebAppButtonWithoutCallbackData()
    {
        var (f, id, _) = await Setup();
        await using var _ = f;

        var r = await f.Click(1, $"movienight:g:{id}");

        var button = ((InlineKeyboardMarkup)r.Edit.Message.ReplyMarkup!)
            .InlineKeyboard.SelectMany(x => x).Single(b => b.Text == "Открыть приложение");
        Assert.Equal("https://example.test/", button.WebApp!.Url);
        Assert.Null(button.CallbackData);
    }

    [Fact]
    public async Task AC6_AfterOwnerLeaves_CardShowsNewOwnerAndOwnerKeyboardForHim()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");
        f.Sender.Clear();

        var r = await f.Click(2, $"movienight:g:{id}");

        Assert.Equal($"Группа «{Name}»\nУчастники (по порядку вступления):\n1. Петя (вы) — владелец\n2. Вася", r.Edit.Message.Text);
        Assert.Equal(OwnerRows(id, multi: true), Rows(r.Edit.Message));
    }

    // ---------------- AC-7 ----------------

    [Fact]
    public async Task AC7_GroupsButton_EditsMessageIntoListAndRepeatedPressIsSameWithoutErrors()
    {
        await using var f = await NewAsync();
        var (id, _) = await f.GroupWithAsync("Альфа", 1, 2);
        f.Sender.Clear();

        var first = await f.Click(1, "movienight:groups", messageId: 9);
        var second = await f.Click(1, "movienight:groups", messageId: 9);

        foreach (var r in new[] { first, second })
        {
            Assert.Empty(r.Sends);
            Assert.Equal(9, r.Edit.MessageId);
            Assert.Equal("Ваши группы:", r.Edit.Message.Text);
            Assert.Equal([[$"★ Альфа · 2|movienight:g:{id}"], [NewGroupBtn]], Rows(r.Edit.Message));
        }

        Assert.DoesNotContain(f.Logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task AC7_GroupsButtonWithoutGroups_EditsIntoEmptyList()
    {
        await using var f = await NewAsync();

        var r = await f.Click(1, "movienight:groups");

        Assert.Equal("У вас пока нет групп. Создайте группу и пригласите друзей.", r.Edit.Message.Text);
        Assert.Equal([[NewGroupBtn]], Rows(r.Edit.Message));
    }

    // ---------------- AC-8 ----------------

    public static IEnumerable<object[]> IdActions() =>
        new[] { "g", "invite", "leave", "leaveok", "rename", "newlink", "newlinkok", "members", "kick", "kickok" }
            .Select(a => new object[] { a });

    private static string Data(string action, string id) =>
        action is "kick" or "kickok" ? $"movienight:{action}:{id}:2" : $"movienight:{action}:{id}";

    private static async Task AssertGroupUntouched(MovieNightFactory f, string id, string code)
    {
        var g = await f.GetGroupAsync(1, id);
        Assert.Equal(Name, g.GetProperty("name").GetString());
        Assert.Equal(code, g.GetProperty("invite").GetProperty("code").GetString());
        Assert.Equal([1L, 2L], MovieNightFactory.MemberIds(g));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes"));
    }

    [Theory]
    [MemberData(nameof(IdActions))]
    public async Task AC8_NonMemberPressesButtonOfForeignGroup_AlertAndListEditedNothingChanged(string action)
    {
        var (f, id, code) = await Setup(members: [2]);
        await using var _ = f;
        await f.GroupWithAsync("Моя", 4);
        f.Sender.Clear();

        var r = await f.Click(4, Data(action, id));

        Assert.Equal(NotInGroup, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Empty(r.Sends);
        Assert.Equal("Ваши группы:", r.Edit.Message.Text);
        Assert.StartsWith("★ Моя · 1|movienight:g:", Rows(r.Edit.Message)[0][0]);
        await AssertGroupUntouched(f, id, code);
        Assert.Equal(NotACommand, (await f.Text(4, "Новое")).SendTo(4).Message.Text);
    }

    [Theory]
    [MemberData(nameof(IdActions))]
    public async Task AC8_NonexistentGuid_AlertAndListEdited(string action)
    {
        await using var f = await NewAsync();

        var r = await f.Click(1, Data(action, Guid.NewGuid().ToString("D")));

        Assert.Equal(NotInGroup, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Equal("У вас пока нет групп. Создайте группу и пригласите друзей.", r.Edit.Message.Text);
        Assert.Empty(r.Sends);
    }

    [Theory]
    [MemberData(nameof(IdActions))]
    public async Task AC8_PayloadIsNotGuidFormatD_TreatedAsGroupNotFound(string action)
    {
        var (f, id, code) = await Setup(members: [2]);
        await using var _ = f;

        foreach (var bad in new[] { "abc", Guid.Parse(id).ToString("N"), $"{{{id}}}", $"({id})", "" })
        {
            var r = await f.Click(1, Data(action, bad));

            Assert.Equal(NotInGroup, r.AnswerText);
            Assert.True(r.AnswerAlert);
            Assert.Equal("Ваши группы:", r.Edit.Message.Text);
            Assert.Empty(r.Sends);
        }

        await AssertGroupUntouched(f, id, code);
    }

    [Theory]
    [MemberData(nameof(IdActions))]
    public async Task AC8_GroupBecameEmpty_AlertAndListEdited(string action)
    {
        var (f, id, _) = await Setup();
        await using var _ = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");
        f.Sender.Clear();

        var r = await f.Click(1, Data(action, id));

        Assert.Equal(NotInGroup, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Equal("У вас пока нет групп. Создайте группу и пригласите друзей.", r.Edit.Message.Text);
        Assert.Empty(r.Sends);
    }

    [Fact]
    public async Task AC8_MembershipLostAfterLeave_OldCardButtonsAnswerNotMemberWithoutChanges()
    {
        var (f, id, _) = await Setup(members: [2]);
        await using var _ = f;
        await f.PostAsync(2, $"/api/movienight/groups/{id}/leave");
        f.Sender.Clear();

        var r = await f.Click(2, $"movienight:g:{id}");

        Assert.Equal(NotInGroup, r.AnswerText);
        Assert.Equal("У вас пока нет групп. Создайте группу и пригласите друзей.", r.Edit.Message.Text);
    }

    // ---------------- AC-9 ----------------

    [Theory]
    [InlineData("rename", "")]
    [InlineData("newlink", "")]
    [InlineData("newlinkok", "")]
    [InlineData("members", "")]
    [InlineData("kick", ":1")]
    [InlineData("kickok", ":1")]
    [InlineData("kick", ":abc")]
    [InlineData("kickok", ":99999999999999999999")]
    public async Task AC9_NonOwnerMemberPressesOwnerButton_AlertCardEditedNothingChanged(string action, string tail)
    {
        var (f, id, code) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(2, $"movienight:{action}:{id}{tail}");

        Assert.Equal(NotOwner, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Empty(r.Sends);
        Assert.Equal($"Группа «{Name}»\nУчастники (по порядку вступления):\n1. Аня — владелец\n2. Петя (вы)\n3. Вася", r.Edit.Message.Text);
        Assert.Equal(MemberRows(id), Rows(r.Edit.Message));
        var g = await f.GetGroupAsync(1, id);
        Assert.Equal(Name, g.GetProperty("name").GetString());
        Assert.Equal(code, g.GetProperty("invite").GetProperty("code").GetString());
        Assert.Equal([1L, 2L, 3L], MovieNightFactory.MemberIds(g));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_revoked_invite_codes"));
        Assert.Equal(NotACommand, (await f.Text(2, "Новое")).SendTo(2).Message.Text);
    }

    // ---------------- AC-10 ----------------

    [Fact]
    public async Task AC10_MemberPressesInvite_SendsNewMessageWithLinksWithoutKeyboardAndEmptyAnswer()
    {
        var (f, id, code) = await Setup(members: [2]);
        await using var _ = f;

        var r = await f.Click(2, $"movienight:invite:{id}");

        Assert.Empty(r.Edits);
        var msg = r.SendTo(2).Message;
        Assert.Equal(InviteText(Name, code), msg.Text);
        Assert.False(msg.Html);
        AssertNoKeyboard(msg);
        Assert.Null(r.AnswerText);
        Assert.False(r.AnswerAlert);
        Assert.Single(r.Sends);
    }

    [Fact]
    public async Task AC10_UsernameNotSet_AlertLinkUnavailableAndNothingSent()
    {
        var (f, id, _) = await Setup(username: null, members: [2]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:invite:{id}");

        Assert.Equal("Ссылка-приглашение недоступна", r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Empty(r.Sends);
    }

    // ---------------- AC-14 ----------------

    [Fact]
    public async Task AC14_MemberPressesLeave_EditsConfirmWithoutChanges()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(2, $"movienight:leave:{id}");

        Assert.Equal($"Выйти из группы «{Name}»?", r.Edit.Message.Text);
        Assert.Equal([["Да, выйти|movienight:leaveok:" + id], ["Отмена|movienight:g:" + id]], Rows(r.Edit.Message));
        var members = MovieNightFactory.MemberIds(await f.GetGroupAsync(1, id));
        Assert.Equal([1L, 2L, 3L], members);
    }

    [Fact]
    public async Task AC14_OwnerWithOthersPressesLeave_ConfirmNamesNextOwnerByPosition()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:leave:{id}");

        Assert.Equal($"Выйти из группы «{Name}»? Владельцем станет Петя.", r.Edit.Message.Text);
    }

    [Fact]
    public async Task AC14_SoleOwnerPressesLeave_ConfirmWithoutOwnershipSentence()
    {
        var (f, id, _) = await Setup();
        await using var _ = f;

        var r = await f.Click(1, $"movienight:leave:{id}");

        Assert.Equal($"Выйти из группы «{Name}»?", r.Edit.Message.Text);
    }

    [Fact]
    public async Task AC14_MemberConfirmsLeave_EditsFarewellWithListButtonAndOthersNotified()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(2, $"movienight:leaveok:{id}", messageId: 55);

        Assert.Equal(55, r.Edit.MessageId);
        Assert.Equal($"Вы вышли из группы «{Name}».", r.Edit.Message.Text);
        Assert.Equal([["« К списку групп|movienight:groups"]], Rows(r.Edit.Message));
        var members = MovieNightFactory.MemberIds(await f.GetGroupAsync(1, id));
        Assert.Equal([1L, 3L], members);
        Assert.Equal([$"Петя больше не в группе «{Name}»."], f.Sender.TextsTo(1));
        Assert.Equal([$"Петя больше не в группе «{Name}»."], f.Sender.TextsTo(3));
        Assert.Empty(r.SendsTo(2));
    }

    [Fact]
    public async Task AC14_OwnerConfirmsLeave_OwnershipPassesToSecondAndNotificationsFollow004()
    {
        var (f, id, _) = await Setup(members: [2, 3]);
        await using var _ = f;

        var r = await f.Click(1, $"movienight:leaveok:{id}");

        Assert.Equal($"Вы вышли из группы «{Name}».", r.Edit.Message.Text);
        var g = await f.GetGroupAsync(2, id);
        Assert.Equal(2L, g.GetProperty("ownerId").GetInt64());
        Assert.Equal([$"Аня больше не в группе «{Name}». Теперь вы владелец группы."], f.Sender.TextsTo(2));
        Assert.Equal([$"Аня больше не в группе «{Name}»."], f.Sender.TextsTo(3));
        Assert.Empty(f.Sender.TextsTo(1));
    }

    [Fact]
    public async Task AC14_LastMemberLeaves_FarewellWithoutNotifications()
    {
        var (f, id, _) = await Setup();
        await using var _ = f;

        var r = await f.Click(1, $"movienight:leaveok:{id}");

        Assert.Equal($"Вы вышли из группы «{Name}».", r.Edit.Message.Text);
        Assert.Empty(f.Sender.Sent);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_group_members"));
    }

    [Fact]
    public async Task AC14_RepeatedLeaveok_SecondGetsNotMemberAlertAndNoDuplicateNotifications()
    {
        var (f, id, _) = await Setup(members: [2]);
        await using var _ = f;

        await f.Click(2, $"movienight:leaveok:{id}");
        var again = await f.Click(2, $"movienight:leaveok:{id}");

        Assert.Equal(NotInGroup, again.AnswerText);
        Assert.True(again.AnswerAlert);
        Assert.Equal("У вас пока нет групп. Создайте группу и пригласите друзей.", again.Edit.Message.Text);
        Assert.Single(f.Sender.TextsTo(1));
    }

    [Fact]
    public async Task AC14_ConcurrentLeaveok_ExactlyOneLeavesOneNotification()
    {
        var (f, id, _) = await Setup(members: [2]);
        await using var _ = f;

        var results = await Task.WhenAll(
            f.Click(2, $"movienight:leaveok:{id}", 1),
            f.Click(2, $"movienight:leaveok:{id}", 2));

        Assert.Single(results, r => r.AnswerText == NotInGroup);
        Assert.Single(f.Sender.TextsTo(1));
    }
}
