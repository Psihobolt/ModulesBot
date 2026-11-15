using System.Net;
using System.Text.Json;
using Bot.Platform.Messaging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Modules.MovieNight.Tests.Helpers;

/// <summary>Результат одного обновления: что добавилось в журнал отправителя и в запросы к Bot API.</summary>
public sealed class BotOutcome(IReadOnlyList<SenderEntry> journal, IReadOnlyList<TelegramCall> calls, string? queryId)
{
    public IReadOnlyList<SenderEntry> Journal => journal;

    public IReadOnlyList<SenderEntry> Sends => journal.Where(e => !e.IsEdit).ToArray();

    public IReadOnlyList<SenderEntry> Edits => journal.Where(e => e.IsEdit).ToArray();

    /// <summary>Единственная новая отправка в чат пользователя.</summary>
    public SenderEntry SendTo(long chatId) => Assert.Single(Sends, e => e.ChatId == chatId);

    public IReadOnlyList<SenderEntry> SendsTo(long chatId) => Sends.Where(e => e.ChatId == chatId).ToArray();

    public SenderEntry Edit => Assert.Single(Edits);

    public IReadOnlyList<TelegramCall> Answers =>
        calls.Where(c => c.Method == "answerCallbackQuery"
            && c.Body.TryGetProperty("callback_query_id", out var id) && id.GetString() == queryId).ToArray();

    private JsonElement AnswerBody => Assert.Single(Answers).Body;

    /// <summary>text ответа на callback; null = поля нет (пустой ответ).</summary>
    public string? AnswerText => AnswerBody.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

    public bool AnswerAlert => AnswerBody.TryGetProperty("show_alert", out var a) && a.ValueKind == JsonValueKind.True;
}

public static class BotDriver
{
    /// <summary>Отправляет обновление через webhook (ожидает 200) и возвращает наблюдаемые эффекты.</summary>
    public static async Task<BotOutcome> Do(this MovieNightFactory f, Update update)
    {
        var journalBefore = f.Sender.Journal.Count;
        var callsBefore = f.BotApi.Calls.Count;
        var response = await f.PostUpdateAsync(update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new BotOutcome(
            f.Sender.Journal.Skip(journalBefore).ToArray(),
            f.BotApi.Calls.Skip(callsBefore).ToArray(),
            update.CallbackQuery?.Id);
    }

    public static Task<BotOutcome> Click(this MovieNightFactory f, long user, string data, int messageId = 4242) =>
        f.Do(BotUpdates.Click(user, data, messageId));

    public static Task<BotOutcome> Text(this MovieNightFactory f, long user, string text) => f.Do(BotUpdates.Text(user, text));

    public static Task<BotOutcome> Cmd(this MovieNightFactory f, long user, string command, string? args = null) =>
        f.Do(BotUpdates.Command(user, command, args));

    public static Task<BotOutcome> Start(this MovieNightFactory f, long user, string? payload = null) =>
        f.Do(BotUpdates.Start(user, payload));

    /// <summary>Фабрика с PublicUrl; пользователи 1..users посеяны; журнал очищен.</summary>
    public static async Task<MovieNightFactory> NewAsync(int users = 4, string? publicUrl = "https://example.test/", string? username = "MovieNightBot")
    {
        var f = new MovieNightFactory { PublicUrl = publicUrl, Username = username };
        await f.SeedDefaultsAsync(users);
        f.Sender.Clear();
        return f;
    }

    /// <summary>Строки клавиатуры: "текст|callback" или "текст|web:url"; пустой массив, если клавиатуры нет.</summary>
    public static string[][] Rows(OutgoingMessage message) =>
        message.ReplyMarkup is InlineKeyboardMarkup m
            ? m.InlineKeyboard.Select(r => r.Select(b => b.Text + "|" + (b.WebApp is { } w ? "web:" + w.Url : b.CallbackData)).ToArray()).ToArray()
            : [];

    public static string[] Buttons(OutgoingMessage message) => Rows(message).SelectMany(r => r).ToArray();

    public static void AssertNoKeyboard(OutgoingMessage message) => Assert.Empty(Buttons(message));

    public const string NotInGroup = "Вы не состоите в этой группе";
    public const string NotOwner = "Это может сделать только владелец группы.";
    public const string NameInvalid = "Название должно быть от 1 до 64 символов, без переводов строк.";
    public const string LimitText = "Можно состоять не больше чем в 20 группах.";
    public const string NotACommand = "Я понимаю только команды. Список команд — /help";
    public const string InviteInvalid = "Приглашение недействительно или группа больше не существует.";
    public const string MemberGone = "Этот участник уже не в группе";
    public const string GroupsHeader = "Ваши группы:";
    public const string NewGroupBtn = "➕ Создать группу|movienight:new";
    public const string WebBtn = "Открыть приложение|web:https://example.test/";

    public static string MiniLink(string code) => $"https://t.me/MovieNightBot/app?startapp=movienight_join_{code}";

    public static string BotLink(string code) => $"https://t.me/MovieNightBot?start=movienight_join_{code}";

    public static string InviteText(string name, string code) =>
        $"Присоединяйся к группе «{name}» в Киновечере!\nОткрыть приложение: {MiniLink(code)}\nЧерез бота: {BotLink(code)}";

}
