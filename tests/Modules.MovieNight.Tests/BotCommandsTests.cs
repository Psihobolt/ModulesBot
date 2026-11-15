using System.Text.Json;
using Modules.MovieNight.Tests.Helpers;
using static Modules.MovieNight.Tests.Helpers.BotDriver;

namespace Modules.MovieNight.Tests;

// TEST-2 (005): AC-1, AC-3, AC-4, AC-5, AC-11
public class BotCommandsTests
{
    private const string Ask = "Как назовём группу? Отправьте название одним сообщением (до 64 символов).";
    private static readonly string Long65 = new('я', 65);

    private static string Card1(string name) => $"Группа «{name}»\nУчастники (по порядку вступления):\n1. Аня (вы) — владелец";

    private static async Task Fill20Async(MovieNightFactory f, long user)
    {
        for (var i = 0; i < 20; i++) await f.CreateGroupAsync(user, "G" + i);
        f.Sender.Clear();
    }

    // ---------------- AC-1 ----------------

    [Fact]
    public async Task AC1_StartWithPublicUrl_SetMyCommandsHasFourCommandsInOrder()
    {
        await using var f = await NewAsync();
        f.CreateClient();

        var call = Assert.Single(f.BotApi.Of("setMyCommands"));
        var commands = call.Body.GetProperty("commands").EnumerateArray()
            .Select(c => (c.GetProperty("command").GetString(), c.GetProperty("description").GetString())).ToArray();
        Assert.Equal(["start", "help", "groups", "newgroup"], commands.Select(c => c.Item1));
        Assert.Equal("мои группы", commands[2].Item2);
        Assert.Equal("создать группу", commands[3].Item2);
    }

    [Fact]
    public async Task AC1_StartWithoutPublicUrl_SetMyCommandsNotCalled()
    {
        await using var f = await NewAsync(publicUrl: null);
        f.CreateClient();

        Assert.Empty(f.BotApi.Calls);
    }

    [Fact]
    public async Task AC1_Help_RepliesWithFourCommands()
    {
        await using var f = await NewAsync();

        var r = await f.Cmd(1, "help");

        Assert.Equal(
            "Команды:\n/start — открыть приложение\n/help — список команд\n/groups — мои группы\n/newgroup — создать группу",
            r.SendTo(1).Message.Text);
    }

    // ---------------- AC-3 ----------------

    [Fact]
    public async Task AC3_NewGroupWithPaddedName_CreatesTrimmedGroupAndSendsCardAsNewMessage()
    {
        await using var f = await NewAsync();

        var r = await f.Do(BotUpdates.Command(1, "newgroup", "  Киноклуб  "));

        Assert.Equal("Киноклуб", f.ScalarString("SELECT Name FROM movienight_groups"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Empty(r.Edits);
        var msg = r.SendTo(1).Message;
        Assert.Equal(Card1("Киноклуб"), msg.Text);
        Assert.False(msg.Html);
        Assert.Contains(WebBtn, Buttons(msg));
    }

    [Fact]
    public async Task AC3_NewGroupWithoutArguments_AsksNameWithCancelAndWaitsForReply()
    {
        await using var f = await NewAsync();

        var r = await f.Cmd(1, "newgroup");

        var msg = r.SendTo(1).Message;
        Assert.Equal(Ask, msg.Text);
        Assert.Equal([["Отмена|movienight:cancel"]], Rows(msg));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));

        var created = await f.Text(1, "Киноклуб");

        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(Card1("Киноклуб"), created.SendTo(1).Message.Text);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public async Task AC3_NewGroupWithLineBreak_RepliesInvalidNameNoGroupNoWaiting(string name)
    {
        await using var f = await NewAsync();

        var r = await f.Cmd(1, "newgroup", name);

        Assert.Equal(NameInvalid, r.SendTo(1).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        var next = await f.Text(1, "Киноклуб");
        Assert.Equal(NotACommand, next.SendTo(1).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Fact]
    public async Task AC3_NewGroupWith65Chars_RepliesInvalidNameNoGroupNoWaiting()
    {
        await using var f = await NewAsync();

        var r = await f.Cmd(1, "newgroup", Long65);

        Assert.Equal(NameInvalid, r.SendTo(1).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(NotACommand, (await f.Text(1, "Киноклуб")).SendTo(1).Message.Text);
    }

    [Fact]
    public async Task AC3_NewGroupWith64Chars_CreatesGroup()
    {
        await using var f = await NewAsync();

        await f.Cmd(1, "newgroup", new string('я', 64));

        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Fact]
    public async Task AC3_NewGroupAtLimit_RepliesLimitNoGroupNoWaiting()
    {
        await using var f = await NewAsync();
        await Fill20Async(f, 1);

        var r = await f.Cmd(1, "newgroup", "Ещё одна");

        Assert.Equal(LimitText, r.SendTo(1).Message.Text);
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(NotACommand, (await f.Text(1, "Ещё")).SendTo(1).Message.Text);
    }

    // ---------------- AC-4 ----------------

    [Fact]
    public async Task AC4_ReplyWithValidName_CreatesGroupAndSendsCardAsNewMessage()
    {
        await using var f = await NewAsync();
        await f.Cmd(1, "newgroup");

        var r = await f.Text(1, "  Киноклуб ");

        Assert.Empty(r.Edits);
        Assert.Equal(Card1("Киноклуб"), r.SendTo(1).Message.Text);
        Assert.Equal("Киноклуб", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    [Fact]
    public async Task AC4_ReplyWithInvalidName_AsksAgainAndNextValidTextCreatesGroup()
    {
        await using var f = await NewAsync();
        await f.Cmd(1, "newgroup");

        var bad = await f.Text(1, Long65);
        var badLine = await f.Text(1, "a\nb");

        Assert.Equal(NameInvalid + " Попробуйте ещё раз.", bad.SendTo(1).Message.Text);
        Assert.Equal(NameInvalid + " Попробуйте ещё раз.", badLine.SendTo(1).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));

        var ok = await f.Text(1, "Киноклуб");
        Assert.Equal(Card1("Киноклуб"), ok.SendTo(1).Message.Text);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Fact]
    public async Task AC4_ReplyWithBlankText_PlatformKeepsWaitingAndNextValidTextCreatesGroup()
    {
        await using var f = await NewAsync();
        await f.Cmd(1, "newgroup");

        await f.Text(1, "   ");
        var ok = await f.Text(1, "Киноклуб");

        Assert.Equal(Card1("Киноклуб"), ok.SendTo(1).Message.Text);
    }

    [Fact]
    public async Task AC4_ReplyAtLimit_RepliesLimitAndWaitingIsNotSet()
    {
        await using var f = await NewAsync();
        await f.Cmd(1, "newgroup");
        await Fill20Async(f, 1);

        var r = await f.Text(1, "Ещё одна");

        Assert.Equal(LimitText, r.SendTo(1).Message.Text);
        Assert.Equal(20, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
        Assert.Equal(NotACommand, (await f.Text(1, "Ещё")).SendTo(1).Message.Text);
    }

    [Fact]
    public async Task AC4_WaitingOfOneUser_DoesNotAffectAnotherUser()
    {
        await using var f = await NewAsync();
        await f.Cmd(1, "newgroup");

        var other = await f.Text(2, "Киноклуб");

        Assert.Equal(NotACommand, other.SendTo(2).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    // ---------------- AC-5 ----------------

    [Fact]
    public async Task AC5_NewButton_SendsQuestionAsNewMessageAndWaitsForReply()
    {
        await using var f = await NewAsync();

        var r = await f.Click(1, "movienight:new");

        Assert.Empty(r.Edits);
        var msg = r.SendTo(1).Message;
        Assert.Equal(Ask, msg.Text);
        Assert.Equal([["Отмена|movienight:cancel"]], Rows(msg));
        Assert.Null(r.AnswerText);
        var created = await f.Text(1, "Киноклуб");
        Assert.Equal(Card1("Киноклуб"), created.SendTo(1).Message.Text);
    }

    [Fact]
    public async Task AC5_Cancel_EditsMessageWithoutKeyboardAndNextTextIsNotACommand()
    {
        await using var f = await NewAsync();
        await f.Click(1, "movienight:new");

        var r = await f.Click(1, "movienight:cancel", messageId: 777);

        Assert.Empty(r.Sends);
        var edit = r.Edit;
        Assert.Equal((1L, 777), (edit.ChatId, edit.MessageId!.Value));
        Assert.Equal("Хорошо, отменили.", edit.Message.Text);
        AssertNoKeyboard(edit.Message);
        Assert.Equal(NotACommand, (await f.Text(1, "Киноклуб")).SendTo(1).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    [Fact]
    public async Task AC5_CancelWithoutWaiting_StillEditsSameWay()
    {
        await using var f = await NewAsync();

        var first = await f.Click(1, "movienight:cancel", messageId: 5);
        var second = await f.Click(1, "movienight:cancel", messageId: 5);

        foreach (var r in new[] { first, second })
        {
            Assert.Equal("Хорошо, отменили.", r.Edit.Message.Text);
            AssertNoKeyboard(r.Edit.Message);
        }
    }

    [Fact]
    public async Task AC5_AnyCommandResetsWaiting()
    {
        await using var f = await NewAsync();
        await f.Cmd(1, "newgroup");
        await f.Cmd(1, "groups");

        var r = await f.Text(1, "Киноклуб");

        Assert.Equal(NotACommand, r.SendTo(1).Message.Text);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM movienight_groups"));
    }

    // ---------------- AC-11 ----------------

    private static async Task<(MovieNightFactory F, string Id)> Setup(long[]? members = null)
    {
        var f = await NewAsync();
        var (id, _) = await f.GroupWithAsync("Мы с Петей", 1, members ?? []);
        f.Sender.Clear();
        return (f, id);
    }

    [Fact]
    public async Task AC11_OwnerRename_AsksNewNameThenRenamesAndSendsCardWithoutNotifications()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var ask = await f.Click(1, $"movienight:rename:{id}");

        Assert.Empty(ask.Edits);
        var q = ask.SendTo(1).Message;
        Assert.Equal("Новое название для группы «Мы с Петей»? Отправьте одним сообщением (до 64 символов).", q.Text);
        Assert.Equal([["Отмена|movienight:cancel"]], Rows(q));

        var done = await f.Text(1, " Киноклуб ");

        Assert.Equal("Киноклуб", f.ScalarString("SELECT Name FROM movienight_groups"));
        var card = done.SendTo(1).Message.Text;
        Assert.Equal("Группа «Киноклуб»\nУчастники (по порядку вступления):\n1. Аня (вы) — владелец\n2. Петя", card);
        Assert.Empty(done.SendsTo(2));
        Assert.Single(done.Sends);
    }

    [Fact]
    public async Task AC11_InvalidName_AsksAgainWithSamePayloadThenValidRenames()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        await f.Click(1, $"movienight:rename:{id}");

        var bad = await f.Text(1, Long65);

        Assert.Equal(NameInvalid + " Попробуйте ещё раз.", bad.SendTo(1).Message.Text);
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
        var ok = await f.Text(1, "Новое");
        Assert.Contains("Группа «Новое»", ok.SendTo(1).Message.Text);
        Assert.Equal("Новое", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    [Fact]
    public async Task AC11_NonOwnerClicksRename_GetsNotOwnerAlertAndNoWaiting()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;

        var r = await f.Click(2, $"movienight:rename:{id}");

        Assert.Equal(NotOwner, r.AnswerText);
        Assert.True(r.AnswerAlert);
        Assert.Empty(r.Sends);
        Assert.Equal(NotACommand, (await f.Text(2, "Новое")).SendTo(2).Message.Text);
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
    }

    [Fact]
    public async Task AC11_OwnerLeftGroupWhileWaiting_ReplySaysNotMemberEvenWithInvalidNameAndStopsWaiting()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        await f.Click(1, $"movienight:rename:{id}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await f.PostAsync(1, $"/api/movienight/groups/{id}/leave")).StatusCode);
        f.Sender.Clear();

        var r = await f.Text(1, Long65);

        Assert.Equal(NotInGroup, r.SendTo(1).Message.Text);
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
        Assert.Equal(NotACommand, (await f.Text(1, "Новое")).SendTo(1).Message.Text);
    }

    [Fact]
    public async Task AC11_OwnershipLostWhileWaiting_ReplySaysOnlyOwnerEvenWithInvalidNameAndStopsWaiting()
    {
        var (f, id) = await Setup([2]);
        await using var _ = f;
        var code = (await f.GetGroupAsync(1, id)).GetProperty("invite").GetProperty("code").GetString()!;
        await f.Click(1, $"movienight:rename:{id}");
        await f.PostAsync(1, $"/api/movienight/groups/{id}/leave");     // владение уходит к Пете
        await f.JoinAsync(1, code);                                       // Аня снова участник, но не владелец
        f.Sender.Clear();

        var r = await f.Text(1, Long65);

        Assert.Equal(NotOwner, r.SendTo(1).Message.Text);
        Assert.Equal("Мы с Петей", f.ScalarString("SELECT Name FROM movienight_groups"));
        Assert.Equal(NotACommand, (await f.Text(1, "Новое")).SendTo(1).Message.Text);
    }

    [Fact]
    public async Task AC11_RenameToSameNameAndUnicode_Succeeds()
    {
        var (f, id) = await Setup();
        await using var _ = f;
        await f.Click(1, $"movienight:rename:{id}");

        var r = await f.Text(1, "Мы с Петей 🎬");

        Assert.Contains("Группа «Мы с Петей 🎬»", r.SendTo(1).Message.Text);
    }
}
