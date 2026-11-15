namespace Bot.Platform.Configuration;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public required string Token { get; set; }

    /// <summary>^[A-Za-z0-9_-]{1,256}$ — значение заголовка X-Telegram-Bot-Api-Secret-Token.</summary>
    public required string WebhookSecret { get; set; }

    /// <summary>Публичный https-адрес Host: база для webhook и кнопки Mini App.</summary>
    public Uri? PublicUrl { get; set; }

    public bool SetupOnStartup { get; set; } = true;

    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(5);
}
