using System.Text.RegularExpressions;
using Bot.Platform.Configuration;
using Microsoft.Extensions.Options;

namespace Bot.Platform.Messaging;

public interface IDeepLinks
{
    /// <summary>https://t.me/{bot}[/{app}]?startapp={module}_{payload}; null, если Bot:Username не задан.</summary>
    string? MiniApp(string module, string payload);

    /// <summary>https://t.me/{bot}?start={module}_{payload}; null, если Bot:Username не задан.</summary>
    string? BotStart(string module, string payload);

    /// <summary>Bot:PublicUrl — для InlineKeyboardButton.WithWebApp.</summary>
    Uri? WebAppUrl { get; }
}

internal sealed partial class DeepLinks(IOptions<BotOptions> options) : IDeepLinks
{
    private const int MaxParameterLength = 64;

    public Uri? WebAppUrl => options.Value.PublicUrl;

    public string? MiniApp(string module, string payload)
    {
        var parameter = BuildParameter(module, payload);
        var o = options.Value;
        if (string.IsNullOrEmpty(o.Username))
        {
            return null;
        }

        return string.IsNullOrEmpty(o.MiniAppShortName)
            ? $"https://t.me/{o.Username}?startapp={parameter}"
            : $"https://t.me/{o.Username}/{o.MiniAppShortName}?startapp={parameter}";
    }

    public string? BotStart(string module, string payload)
    {
        var parameter = BuildParameter(module, payload);
        return string.IsNullOrEmpty(options.Value.Username)
            ? null
            : $"https://t.me/{options.Value.Username}?start={parameter}";
    }

    private static string BuildParameter(string module, string payload)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(payload);
        if (!ModulePattern().IsMatch(module))
        {
            throw new ArgumentException("Module name must match ^[a-z][a-z0-9]{1,31}$.", nameof(module));
        }

        if (!PayloadPattern().IsMatch(payload))
        {
            throw new ArgumentException("Payload must match ^[A-Za-z0-9_-]+$.", nameof(payload));
        }

        var parameter = $"{module}_{payload}";
        if (parameter.Length > MaxParameterLength)
        {
            throw new ArgumentException($"Link parameter must not exceed {MaxParameterLength} characters.", nameof(payload));
        }

        return parameter;
    }

    [GeneratedRegex("^[a-z][a-z0-9]{1,31}\\z")]
    private static partial Regex ModulePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]+\\z")]
    private static partial Regex PayloadPattern();
}
