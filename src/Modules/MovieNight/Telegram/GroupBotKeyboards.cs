using System.Globalization;
using Bot.Platform.Messaging;
using Modules.MovieNight.Application;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Modules.MovieNight.Telegram;

internal static class GroupBotKeyboards
{
    private const string Module = "movienight";

    public static string Data(string action, string? payload = null) => CallbackData.Create(Module, action, payload);

    public static string GroupData(string action, Guid id) => Data(action, id.ToString("D"));

    public static string MemberData(string action, Guid id, long joinSeq) =>
        Data(action, id.ToString("D") + ":" + joinSeq.ToString(CultureInfo.InvariantCulture));

    public static InlineKeyboardMarkup GroupList(IReadOnlyList<GroupSummary> groups)
    {
        var rows = groups
            .Select(g => Row(
                $"{(g.IsOwner ? "★ " : string.Empty)}{g.Name} · {g.MemberCount.ToString(CultureInfo.InvariantCulture)}",
                GroupData("g", g.Id)))
            .ToList();
        rows.Add(Row(GroupBotTexts.CreateButton, Data("new")));
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup GroupCard(GroupDetails group, long viewerId, Uri? webAppUrl)
    {
        var rows = new List<InlineKeyboardButton[]> { Row(GroupBotTexts.InviteButton, GroupData("invite", group.Id)) };
        if (group.OwnerId == viewerId)
        {
            rows.Add(Row(GroupBotTexts.RenameButton, GroupData("rename", group.Id)));
            rows.Add(Row(GroupBotTexts.NewLinkButton, GroupData("newlink", group.Id)));
            if (group.Members.Count > 1)
            {
                rows.Add(Row(GroupBotTexts.MembersButton, GroupData("members", group.Id)));
            }
        }

        rows.Add(Row(GroupBotTexts.LeaveButton, GroupData("leave", group.Id)));
        rows.Add(Row(GroupBotTexts.ToListButton, Data("groups")));
        if (webAppUrl is not null)
        {
            rows.Add([InlineKeyboardButton.WithWebApp(GroupBotTexts.OpenAppButton, new WebAppInfo(webAppUrl.OriginalString))]);
        }

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup Members(GroupDetails group, long viewerId)
    {
        var rows = group.Members
            .Where(m => m.UserId != viewerId)
            .Select(m => Row($"Исключить {m.DisplayName}", MemberData("kick", group.Id, m.JoinSeq)))
            .ToList();
        rows.Add(Row(GroupBotTexts.ToGroupButton, GroupData("g", group.Id)));
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup Confirm(string yesText, string yesData, string noData) =>
        new([Row(yesText, yesData), Row(GroupBotTexts.CancelButton, noData)]);

    public static InlineKeyboardMarkup JoinPrompt(string code) =>
        new([Row(GroupBotTexts.JoinButton, Data("join", code)), Row(GroupBotTexts.NotNowButton, Data("nojoin"))]);

    public static InlineKeyboardMarkup Cancel() => new([Row(GroupBotTexts.CancelButton, Data("cancel"))]);

    public static InlineKeyboardMarkup Single(string text, string data) => new([Row(text, data)]);

    private static InlineKeyboardButton[] Row(string text, string data) => [InlineKeyboardButton.WithCallbackData(text, data)];
}
