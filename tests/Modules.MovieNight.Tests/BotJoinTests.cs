using Modules.MovieNight.Tests.Helpers;
using static Modules.MovieNight.Tests.Helpers.BotDriver;

namespace Modules.MovieNight.Tests;

// TEST-5 (005): AC-15, AC-16
public class BotJoinTests
{
    private const string Name = "Мы с Петей";
    private const string Code = "AB12cd34EF";

    private static async Task<(MovieNightFactory F, string Id)> Setup(params long[] members)
    {
        var f = await NewAsync(5);
        f.Codes.Enqueue(Code);
        var (id, _) = await f.GroupWithAsync(Name, 1, members);
        f.Sender.Clear();
        return (f, id);
    }

    private static long Count(MovieNightFactory f, string where) =>
        f.Scalar($"SELECT COUNT(*) FROM movienight_group_members WHERE {where}");

    // ---------------- AC-15 ----------------

    [Fact]
    public async Task AC15_StartWithValidCodeByNonMember_SendsPreviewWithJoinAndNotNowWithoutJoining()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Start(4, "movienight_join_" + Code);

        Assert.Empty(r.Edits);
        var msg = r.SendTo(4).Message;
        Assert.Equal($"Приглашение в группу «{Name}»\nУчастники: Аня, Петя", msg.Text);
        Assert.False(msg.Html);
        Assert.Equal([[$"Вступить|movienight:join:{Code}"], ["Не сейчас|movienight:nojoin"]], Rows(msg));
        Assert.Equal(0, Count(f, "UserId = 4"));
        Assert.Empty(f.Sender.TextsTo(1));
    }

    [Fact]
    public async Task AC15_StartByExistingMember_SendsAlreadyInGroupWithOpenGroupButton()
    {
        var (f, id) = await Setup(2);
        await using var _ = f;

        var r = await f.Start(2, "movienight_join_" + Code);

        var msg = r.SendTo(2).Message;
        Assert.Equal($"Вы уже в группе «{Name}».", msg.Text);
        Assert.Equal([[$"Открыть группу|movienight:g:{id}"]], Rows(msg));
        Assert.Equal(2, Count(f, "1 = 1"));
    }

    [Theory]
    [InlineData("movienight_join_short")]
    [InlineData("movienight_join_AB12cd34EFX")]
    [InlineData("movienight_join_AB12cd34E-")]
    [InlineData("movienight_join_ZZZZZZZZZZ")]
    [InlineData("movienight_join_ab12cd34ef")]
    [InlineData("movienight_foo")]
    [InlineData("movienight_join")]
    public async Task AC15_StartWithMalformedUnknownOrWrongCaseCode_SendsInvalidWithoutKeyboard(string payload)
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Start(4, payload);

        var msg = r.SendTo(4).Message;
        Assert.Equal(InviteInvalid, msg.Text);
        AssertNoKeyboard(msg);
        Assert.Equal(0, Count(f, "UserId = 4"));
    }

    [Fact]
    public async Task AC15_StartWithPayloadWithoutModule_PlatformWelcomeNotModule()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Start(4, "foo");

        Assert.StartsWith("Привет!", r.SendTo(4).Message.Text);
    }

    [Fact]
    public async Task AC15_StartWithRegeneratedOldCode_Invalid()
    {
        var (f, id) = await Setup(2);
        await using var _ = f;
        f.Codes.Enqueue("NEWcode123");
        await f.Click(1, $"movienight:newlinkok:{id}");
        f.Sender.Clear();

        var r = await f.Start(4, "movienight_join_" + Code);

        Assert.Equal(InviteInvalid, r.SendTo(4).Message.Text);
    }

    [Fact]
    public async Task AC15_StartWithCodeOfEmptyGroup_Invalid()
    {
        var (f, id) = await Setup();
        await using var _ = f;
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");
        f.Sender.Clear();

        var r = await f.Start(4, "movienight_join_" + Code);

        Assert.Equal(InviteInvalid, r.SendTo(4).Message.Text);
    }

    // ---------------- AC-16 ----------------

    [Fact]
    public async Task AC16_JoinButton_JoinsEditsCardAndNotifiesExistingMembersNotExecutor()
    {
        var (f, id) = await Setup(2);
        await using var _ = f;

        var r = await f.Click(4, $"movienight:join:{Code}", messageId: 88);

        Assert.DoesNotContain(r.Sends, s => s.ChatId == 4);
        Assert.Equal((4L, 88), (r.Edit.ChatId, r.Edit.MessageId!.Value));
        Assert.Equal($"Группа «{Name}»\nУчастники (по порядку вступления):\n1. Аня — владелец\n2. Петя\n3. Гоша (вы)", r.Edit.Message.Text);
        Assert.Equal(
            [[$"Пригласить|movienight:invite:{id}"], [$"Выйти из группы|movienight:leave:{id}"], ["« К списку групп|movienight:groups"], [WebBtn]],
            Rows(r.Edit.Message));
        Assert.Equal(1, Count(f, "UserId = 4"));
        Assert.Equal([$"Гоша теперь в группе «{Name}»."], f.Sender.TextsTo(1));
        Assert.Equal([$"Гоша теперь в группе «{Name}»."], f.Sender.TextsTo(2));
        Assert.Empty(f.Sender.TextsTo(4));
    }

    [Fact]
    public async Task AC16_JoinWhenAlreadyMember_EditsCardWithoutNotificationsOrDuplicates()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Click(2, $"movienight:join:{Code}");

        Assert.StartsWith($"Группа «{Name}»", r.Edit.Message.Text);
        Assert.Empty(f.Sender.Sent);
        Assert.Equal(1, Count(f, "UserId = 2"));
    }

    [Fact]
    public async Task AC16_DoubleJoinButton_SecondIsNoOpWithoutSecondNotification()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        await f.Click(4, $"movienight:join:{Code}");
        var second = await f.Click(4, $"movienight:join:{Code}");

        Assert.StartsWith($"Группа «{Name}»", second.Edit.Message.Text);
        Assert.Equal(1, Count(f, "UserId = 4"));
        Assert.Single(f.Sender.TextsTo(1));
        Assert.Single(f.Sender.TextsTo(2));
    }

    [Fact]
    public async Task AC16_TwoUsersJoinConcurrently_BothJoinedEachExistingMemberGetsTwoNotifications()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        await Task.WhenAll(f.Click(4, $"movienight:join:{Code}", 1), f.Click(5, $"movienight:join:{Code}", 2));

        Assert.Equal(1, Count(f, "UserId = 4"));
        Assert.Equal(1, Count(f, "UserId = 5"));
        Assert.Equal(2, f.Sender.TextsTo(1).Count);
        Assert.Equal(2, f.Sender.TextsTo(2).Count);
        // Тот, кто вступил вторым, уведомляет первого: ровно одно уведомление на двоих.
        Assert.Equal(1, f.Sender.TextsTo(4).Count + f.Sender.TextsTo(5).Count);
    }

    [Theory]
    [InlineData("ZZZZZZZZZZ")]
    [InlineData("ab12cd34ef")]
    [InlineData("short")]
    [InlineData("AB12cd34EFX")]
    public async Task AC16_JoinWithUnknownOrMalformedCode_EditsInvalidWithoutKeyboardNobodyJoins(string code)
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Click(4, $"movienight:join:{code}");

        Assert.Equal(InviteInvalid, r.Edit.Message.Text);
        AssertNoKeyboard(r.Edit.Message);
        Assert.Equal(0, Count(f, "UserId = 4"));
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC16_JoinWithoutCodePayload_EditsInvalid()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Click(4, "movienight:join");

        Assert.Equal(InviteInvalid, r.Edit.Message.Text);
    }

    [Fact]
    public async Task AC16_JoinWithOldCodeAfterRegeneration_EditsInvalid()
    {
        var (f, id) = await Setup(2);
        await using var _ = f;
        f.Codes.Enqueue("NEWcode123");
        await f.Click(1, $"movienight:newlinkok:{id}");
        f.Sender.Clear();

        var r = await f.Click(4, $"movienight:join:{Code}");

        Assert.Equal(InviteInvalid, r.Edit.Message.Text);
        Assert.Equal(0, Count(f, "UserId = 4"));
    }

    [Fact]
    public async Task AC16_UserAtGroupLimit_AlertLimitMessageUnchangedNotJoined()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;
        for (var i = 0; i < 20; i++) await f.CreateGroupAsync(4, "G" + i);
        f.Sender.Clear();

        var r = await f.Click(4, $"movienight:join:{Code}");

        Assert.Equal(LimitText, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Empty(r.Journal);
        Assert.Equal(20, Count(f, "UserId = 4"));
        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task AC16_NotNow_EditsWithoutKeyboardAndDoesNotJoin()
    {
        var (f, _) = await Setup(2);
        await using var _ = f;

        var r = await f.Click(4, "movienight:nojoin", messageId: 3);

        Assert.Equal(3, r.Edit.MessageId);
        Assert.Equal("Хорошо, не вступаем.", r.Edit.Message.Text);
        AssertNoKeyboard(r.Edit.Message);
        Assert.Equal(0, Count(f, "UserId = 4"));
        Assert.Empty(r.Sends);
    }
}
