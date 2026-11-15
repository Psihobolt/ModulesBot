namespace Bot.Platform.Messaging;

internal static class BotDefaults
{
    public const string HttpClientName = "telegram";
    public const string WebhookPath = "/bot/webhook";
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";
}
