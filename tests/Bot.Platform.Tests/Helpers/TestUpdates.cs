using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bot.Platform.Tests.Helpers;

/// <summary>
/// Builds Telegram <see cref="Update"/> objects (via raw Bot API JSON, so the shape is exactly what Telegram sends).
/// <see cref="ToJson"/> returns the raw JSON for HTTP tests.
/// </summary>
public static class TestUpdates
{
    private static readonly ConditionalWeakTable<Update, string> RawJson = new();
    private static int _updateId = 1000;
    private static int _messageId = 500;

    public const long DefaultUserId = 1001;

    public static string ToJson(Update update) =>
        RawJson.TryGetValue(update, out var raw) ? raw : JsonSerializer.Serialize(update, JsonBotAPI.Options);

    public static Update Parse(string json)
    {
        var update = JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)
            ?? throw new InvalidOperationException("null update");
        RawJson.AddOrUpdate(update, json);
        return update;
    }

    private static JsonObject User(long id, string firstName, string? lastName, string? username, string? lang, bool premium)
    {
        var user = new JsonObject { ["id"] = id, ["is_bot"] = false, ["first_name"] = firstName };
        if (lastName is not null) user["last_name"] = lastName;
        if (username is not null) user["username"] = username;
        if (lang is not null) user["language_code"] = lang;
        if (premium) user["is_premium"] = true;
        return user;
    }

    private static JsonObject Chat(long id, string type, string firstName) =>
        type == "private"
            ? new JsonObject { ["id"] = id, ["type"] = type, ["first_name"] = firstName }
            : new JsonObject { ["id"] = id, ["type"] = type, ["title"] = "Chat " + id };

    private static JsonObject MessageNode(long chatId, string chatType, long? fromId, string firstName, string? lastName,
        string? username, string? lang, bool premium, Action<JsonObject>? body)
    {
        var node = new JsonObject
        {
            ["message_id"] = Interlocked.Increment(ref _messageId),
            ["date"] = 1_700_000_000,
            ["chat"] = Chat(chatId, chatType, firstName),
        };
        if (fromId is { } f) node["from"] = User(f, firstName, lastName, username, lang, premium);
        body?.Invoke(node);
        return node;
    }

    private static Update Wrap(string field, JsonNode content)
    {
        var root = new JsonObject { ["update_id"] = Interlocked.Increment(ref _updateId), [field] = content };
        return Parse(root.ToJsonString());
    }

    /// <summary>Generic message. Defaults: private chat of the user, user "Иван Петров" (@ivan, ru, premium).</summary>
    public static Update Message(
        string? text,
        long userId = DefaultUserId,
        long? chatId = null,
        string chatType = "private",
        string field = "message",
        bool withFrom = true,
        string firstName = "Иван",
        string? lastName = "Петров",
        string? username = "ivan",
        string? languageCode = "ru",
        bool isPremium = true,
        Action<JsonObject>? extra = null) =>
        Wrap(field, MessageNode(chatId ?? userId, chatType, withFrom ? userId : null, firstName, lastName, username,
            languageCode, isPremium, node =>
            {
                if (text is not null) node["text"] = text;
                extra?.Invoke(node);
            }));

    public static Update Private(string text, long userId = DefaultUserId, string firstName = "Иван",
        string? lastName = "Петров", string? username = "ivan", string? languageCode = "ru", bool isPremium = true) =>
        Message(text, userId, firstName: firstName, lastName: lastName, username: username,
            languageCode: languageCode, isPremium: isPremium);

    /// <summary>"/command args" (with optional "@bot" suffix).</summary>
    public static Update Command(string command, string? args = null, long userId = DefaultUserId, string? botSuffix = null) =>
        Private("/" + command + (botSuffix is null ? "" : "@" + botSuffix) + (args is null ? "" : " " + args), userId);

    public static Update Start(string? payload = null, long userId = DefaultUserId) =>
        Private(payload is null ? "/start" : "/start " + payload, userId);

    public static Update Group(string text, long userId = DefaultUserId, long chatId = -100500, string type = "group") =>
        Message(text, userId, chatId, type);

    public static Update Channel(string text, long chatId = -100777) =>
        Message(text, userId: chatId, chatId: chatId, chatType: "channel", withFrom: false);

    public static Update ChannelPost(string text, long chatId = -100777) =>
        Message(text, userId: chatId, chatId: chatId, chatType: "channel", field: "channel_post", withFrom: false);

    public static Update Edited(string text, long userId = DefaultUserId) =>
        Message(text, userId, field: "edited_message");

    public static Update WithoutFrom(string text, long chatId = DefaultUserId) =>
        Message(text, chatId, chatId, withFrom: false);

    public static Update Sticker(long userId = DefaultUserId) =>
        Message(null, userId, extra: n => n["sticker"] = new JsonObject
        {
            ["file_id"] = "f",
            ["file_unique_id"] = "u",
            ["type"] = "regular",
            ["width"] = 512,
            ["height"] = 512,
            ["is_animated"] = false,
            ["is_video"] = false,
        });

    public static Update PhotoWithCaption(string caption, long userId = DefaultUserId) =>
        Message(null, userId, extra: n =>
        {
            n["caption"] = caption;
            n["photo"] = new JsonArray(new JsonObject
            {
                ["file_id"] = "p",
                ["file_unique_id"] = "pu",
                ["width"] = 10,
                ["height"] = 10,
            });
        });

    /// <summary>callback_query with a message in the given chat (default: private chat of the user).</summary>
    public static Update Callback(string? data, long userId = DefaultUserId, string queryId = "cq-1",
        string chatType = "private", long? chatId = null, bool withMessage = true, string firstName = "Иван",
        string? lastName = "Петров", string? username = "ivan", string? languageCode = "ru", bool isPremium = true)
    {
        var query = new JsonObject
        {
            ["id"] = queryId,
            ["from"] = User(userId, firstName, lastName, username, languageCode, isPremium),
            ["chat_instance"] = "ci",
        };
        if (data is not null) query["data"] = data;
        if (withMessage)
        {
            query["message"] = MessageNode(chatId ?? userId, chatType, 999, "Bot", null, "the_bot", null, false,
                n => n["text"] = "keyboard");
        }

        return Wrap("callback_query", query);
    }

    /// <summary>inline_query: an update type the bot must ignore.</summary>
    public static Update InlineQuery(long userId = DefaultUserId) =>
        Wrap("inline_query", new JsonObject
        {
            ["id"] = "iq",
            ["from"] = User(userId, "Иван", null, null, null, false),
            ["query"] = "q",
            ["offset"] = "",
        });
}
