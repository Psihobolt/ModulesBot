using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bot.Platform.Configuration;
using Bot.Platform.Time;
using Microsoft.Extensions.Options;

namespace Bot.Platform.Auth;

internal sealed class InitDataValidator(
    IOptions<BotOptions> botOptions,
    IOptions<AuthOptions> authOptions,
    IClock clock) : IInitDataValidator
{
    internal const int MaxLength = 16_384;

    private static readonly byte[] WebAppDataKey = "WebAppData"u8.ToArray();

    private static InitDataValidationResult Fail(InitDataError error) => new(null, error);

    public InitDataValidationResult Validate(string? rawInitData)
    {
        if (rawInitData is null)
        {
            return Fail(InitDataError.Missing);
        }

        if (rawInitData.Length is 0 or > MaxLength)
        {
            return Fail(InitDataError.Invalid);
        }

        var fields = ParseFields(rawInitData);
        if (fields is null || !fields.TryGetValue("hash", out var hashHex))
        {
            return Fail(InitDataError.Invalid);
        }

        byte[] providedHash;
        try
        {
            providedHash = Convert.FromHexString(hashHex);
        }
        catch (FormatException)
        {
            return Fail(InitDataError.Invalid);
        }

        // signature (Ed25519 для сторонней проверки) входит в data_check_string, hash — нет.
        var dataCheckString = string.Join(
            '\n',
            fields.Where(f => f.Key != "hash")
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key}={f.Value}"));

        var secretKey = HMACSHA256.HashData(WebAppDataKey, Encoding.UTF8.GetBytes(botOptions.Value.Token));
        var expectedHash = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, providedHash))
        {
            return Fail(InitDataError.Invalid);
        }

        if (!fields.TryGetValue("auth_date", out var authDateRaw)
            || !long.TryParse(authDateRaw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var authDateSeconds)
            || authDateSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return Fail(InitDataError.Invalid);
        }

        if (!fields.TryGetValue("user", out var userJson) || !TryParseUser(userJson, out var user))
        {
            return Fail(InitDataError.Invalid);
        }

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateSeconds);
        var now = clock.UtcNow;
        var options = authOptions.Value;
        if (now - authDate > options.InitDataMaxAge)
        {
            return Fail(InitDataError.Expired);
        }

        if (authDate - now > options.AllowedClockSkew)
        {
            return Fail(InitDataError.Invalid);
        }

        var data = new InitData(
            user,
            authDate,
            fields.GetValueOrDefault("start_param"),
            fields.GetValueOrDefault("query_id"),
            fields.GetValueOrDefault("chat_type"),
            fields.GetValueOrDefault("chat_instance"));
        return new InitDataValidationResult(data, null);
    }

    private static Dictionary<string, string>? ParseFields(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in raw.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                return null;
            }

            var key = WebUtility.UrlDecode(pair[..separator]);
            var value = WebUtility.UrlDecode(pair[(separator + 1)..]);
            if (!result.TryAdd(key, value))
            {
                return null;
            }
        }

        return result;
    }

    private static bool TryParseUser(string json, out TelegramUser user)
    {
        user = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt64(out var id))
            {
                return false;
            }

            user = new TelegramUser(
                id,
                GetString(root, "first_name") ?? string.Empty,
                GetString(root, "last_name"),
                GetString(root, "username"),
                GetString(root, "language_code"),
                GetBool(root, "is_premium") ?? false,
                GetBool(root, "allows_write_to_pm"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
