using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bot.Platform.Tests.Helpers;

/// <summary>Builds and signs Telegram initData strings with a test bot token (algorithm from docs/tasks/001 "Авторизация").</summary>
public static class TestInitData
{
    public const string BotToken = "123456:TEST-TOKEN";
    public const string OtherBotToken = "654321:ANOTHER-BOT-TOKEN";

    /// <summary>Default "now" of <see cref="FakeClock"/> and default auth_date of generated initData.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static string For(
        long userId,
        DateTimeOffset? authDate = null,
        string? startParam = null,
        IReadOnlyDictionary<string, string>? extra = null,
        string firstName = "Test",
        string? lastName = "User",
        string? username = "tester",
        string? languageCode = "ru",
        bool? allowsWriteToPm = null,
        string token = BotToken)
        => Sign(Fields(userId, authDate, startParam, extra, firstName, lastName, username, languageCode, allowsWriteToPm), token);

    public static List<KeyValuePair<string, string>> Fields(
        long userId,
        DateTimeOffset? authDate = null,
        string? startParam = null,
        IReadOnlyDictionary<string, string>? extra = null,
        string firstName = "Test",
        string? lastName = "User",
        string? username = "tester",
        string? languageCode = "ru",
        bool? allowsWriteToPm = null)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("auth_date", Unix(authDate ?? DefaultNow)),
            new("query_id", "AAHdF6IQAAAAAN0XohDhrOrc"),
            new("user", UserJson(userId, firstName, lastName, username, languageCode, allowsWriteToPm)),
        };
        if (startParam is not null)
        {
            fields.Add(new("start_param", startParam));
        }

        if (extra is not null)
        {
            fields.AddRange(extra);
        }

        return fields;
    }

    public static string Unix(DateTimeOffset value) =>
        value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static string UserJson(
        long id, string firstName, string? lastName = null, string? username = null,
        string? languageCode = null, bool? allowsWriteToPm = null)
    {
        var user = new Dictionary<string, object> { ["id"] = id, ["first_name"] = firstName, ["is_premium"] = false };
        if (lastName is not null) user["last_name"] = lastName;
        if (username is not null) user["username"] = username;
        if (languageCode is not null) user["language_code"] = languageCode;
        if (allowsWriteToPm is not null) user["allows_write_to_pm"] = allowsWriteToPm.Value;
        return JsonSerializer.Serialize(user);
    }

    /// <summary>
    /// Signs the fields (any existing "hash" is dropped). <paramref name="excludeFromCheck"/> lets a test
    /// produce a deliberately wrong signature (e.g. computed without "signature").
    /// </summary>
    public static string Sign(IEnumerable<KeyValuePair<string, string>> fields, string token = BotToken, params string[] excludeFromCheck)
    {
        var list = fields.Where(f => f.Key != "hash").ToList();
        var dataCheckString = string.Join(
            '\n',
            list.Where(f => !excludeFromCheck.Contains(f.Key))
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key}={f.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString)));
        list.Add(new("hash", hash));
        return Encode(list);
    }

    public static string Encode(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    public static List<KeyValuePair<string, string>> Decode(string raw) =>
        raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var i = p.IndexOf('=');
                return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(p[..i]), Uri.UnescapeDataString(p[(i + 1)..]));
            })
            .ToList();

    /// <summary>Changes a field value WITHOUT re-signing (the old hash stays) — simulates tampering.</summary>
    public static string Replace(string raw, string key, string newValue) =>
        Encode(Decode(raw).Select(f => f.Key == key ? new KeyValuePair<string, string>(key, newValue) : f));

    public static string Without(string raw, string key) => Encode(Decode(raw).Where(f => f.Key != key));

    public static string GetField(string raw, string key) => Decode(raw).First(f => f.Key == key).Value;

    public static string FlipHash(string raw)
    {
        var hash = GetField(raw, "hash");
        var flipped = (hash[0] == '0' ? '1' : '0') + hash[1..];
        return Replace(raw, "hash", flipped);
    }
}
