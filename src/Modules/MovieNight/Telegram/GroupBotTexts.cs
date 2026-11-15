using System.Text;
using Modules.MovieNight.Application;

namespace Modules.MovieNight.Telegram;

internal static class GroupBotTexts
{
    public const string GroupsTitle = "Ваши группы:";
    public const string NoGroups = "У вас пока нет групп. Создайте группу и пригласите друзей.";
    public const string AskName = "Как назовём группу? Отправьте название одним сообщением (до 64 символов).";
    public const string InvalidName = "Название должно быть от 1 до 64 символов, без переводов строк.";
    public const string InvalidNameRetry = InvalidName + " Попробуйте ещё раз.";
    public const string GroupLimit = "Можно состоять не больше чем в 20 группах.";
    public const string Cancelled = "Хорошо, отменили.";
    public const string NotMember = "Вы не состоите в этой группе";
    public const string NotOwner = "Это может сделать только владелец группы.";
    public const string MemberGone = "Этот участник уже не в группе";
    public const string CannotRemoveSelf = "Чтобы уйти из группы, используйте «Выйти».";
    public const string InviteUnavailable = "Ссылка-приглашение недоступна";
    public const string NewLinkConfirm = "Старая ссылка перестанет работать. Создать новую?";
    public const string NewLinkCreated = "Создана новая ссылка";
    public const string InviteInvalid = "Приглашение недействительно или группа больше не существует.";
    public const string NotJoining = "Хорошо, не вступаем.";

    public const string CreateButton = "➕ Создать группу";
    public const string CancelButton = "Отмена";
    public const string InviteButton = "Пригласить";
    public const string LeaveButton = "Выйти из группы";
    public const string ToListButton = "« К списку групп";
    public const string OpenAppButton = "Открыть приложение";
    public const string RenameButton = "Переименовать";
    public const string NewLinkButton = "Новая ссылка";
    public const string MembersButton = "Участники";
    public const string NewLinkYesButton = "Да, новая ссылка";
    public const string ToGroupButton = "« К группе";
    public const string KickYesButton = "Да, исключить";
    public const string LeaveYesButton = "Да, выйти";
    public const string JoinButton = "Вступить";
    public const string NotNowButton = "Не сейчас";
    public const string OpenGroupButton = "Открыть группу";

    public static string AskRename(string name) =>
        $"Новое название для группы «{name}»? Отправьте одним сообщением (до 64 символов).";

    public static string Card(GroupDetails group, long viewerId)
    {
        var sb = new StringBuilder();
        sb.Append("Группа «").Append(group.Name).Append("»\nУчастники (по порядку вступления):");
        foreach (var m in group.Members)
        {
            sb.Append('\n').Append(m.Position).Append(". ").Append(m.DisplayName);
            if (m.UserId == viewerId)
            {
                sb.Append(" (вы)");
            }

            if (m.IsOwner)
            {
                sb.Append(" — владелец");
            }
        }

        return sb.ToString();
    }

    public static string Invite(GroupDetails group) =>
        $"Присоединяйся к группе «{group.Name}» в Киновечере!\nОткрыть приложение: {group.MiniAppLink}\nЧерез бота: {group.BotLink}";

    public static string MembersTitle(string groupName) => $"Участники группы «{groupName}»:";

    public static string KickConfirm(string memberName, string groupName) =>
        $"Исключить {memberName} из группы «{groupName}»?";

    public static string Kicked(string memberName) => $"{memberName} исключён(а) из группы";

    public static string LeaveConfirm(GroupDetails group, long viewerId)
    {
        var text = $"Выйти из группы «{group.Name}»?";
        if (group.OwnerId == viewerId && group.Members.Count > 1)
        {
            text += $" Владельцем станет {group.Members[1].DisplayName}.";
        }

        return text;
    }

    public static string Left(string groupName) => $"Вы вышли из группы «{groupName}».";

    public static string InvitePreview(InvitePreview preview) =>
        $"Приглашение в группу «{preview.GroupName}»\nУчастники: {string.Join(", ", preview.MemberNames)}";

    public static string AlreadyMember(string groupName) => $"Вы уже в группе «{groupName}».";
}
