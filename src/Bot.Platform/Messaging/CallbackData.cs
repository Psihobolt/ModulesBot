using System.Text;
using System.Text.RegularExpressions;

namespace Bot.Platform.Messaging;

public static partial class CallbackData
{
    public const int MaxBytes = 64;

    public static string Create(string module, string action, string? payload = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(action);
        if (!ModulePattern().IsMatch(module))
        {
            throw new ArgumentException("Module name must match ^[a-z][a-z0-9]{1,31}$.", nameof(module));
        }

        if (!BotNames.IsValidName(action))
        {
            throw new ArgumentException("Action must match ^[a-z0-9_]{1,32}$.", nameof(action));
        }

        var data = string.IsNullOrEmpty(payload) ? $"{module}:{action}" : $"{module}:{action}:{payload}";
        if (Encoding.UTF8.GetByteCount(data) > MaxBytes)
        {
            throw new ArgumentException($"Callback data must not exceed {MaxBytes} bytes in UTF-8.", nameof(payload));
        }

        return data;
    }

    public static bool TryParse(string? data, out string module, out string action, out string payload)
    {
        module = action = payload = string.Empty;
        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        var first = data.IndexOf(':', StringComparison.Ordinal);
        if (first <= 0)
        {
            return false;
        }

        var rest = data.AsSpan(first + 1);
        var second = rest.IndexOf(':');
        var actionPart = second < 0 ? rest : rest[..second];
        if (actionPart.IsEmpty)
        {
            return false;
        }

        module = data[..first];
        action = actionPart.ToString();
        payload = second < 0 ? string.Empty : rest[(second + 1)..].ToString();
        return true;
    }

    [GeneratedRegex("^[a-z][a-z0-9]{1,31}$")]
    private static partial Regex ModulePattern();
}

internal static partial class BotNames
{
    public static bool IsValidName(string value) => NamePattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9_]{1,32}\\z")]
    private static partial Regex NamePattern();
}
