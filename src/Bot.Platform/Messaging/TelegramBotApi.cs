using Bot.Platform.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Messaging;

internal sealed class TelegramBotApi : IBotApi
{
    private readonly Lazy<ITelegramBotClient> _client;

    public TelegramBotApi(IHttpClientFactory httpClientFactory, IOptions<BotOptions> options)
    {
        // Lazy: неверный формат токена не должен ронять создание сервиса до первого вызова.
        _client = new Lazy<ITelegramBotClient>(() =>
        {
            // RetryCount = 0 выключает встроенный повтор при 429: политика повтора — только в MessageSender.
            var clientOptions = new TelegramBotClientOptions(options.Value.Token) { RetryCount = 0 };
            return new TelegramBotClient(clientOptions, httpClientFactory.CreateClient(BotDefaults.HttpClientName));
        });
    }

    public async Task<int> SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup, bool html, CancellationToken ct)
    {
        var message = await _client.Value.SendMessage(
            chatId,
            text,
            parseMode: html ? ParseMode.Html : ParseMode.None,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
        return message.Id;
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, bool showAlert, CancellationToken ct) =>
        await _client.Value.AnswerCallbackQuery(callbackQueryId, text, showAlert, cancellationToken: ct);

    public async Task SetWebhookAsync(string url, string secretToken, IReadOnlyCollection<UpdateType> allowedUpdates, CancellationToken ct) =>
        await _client.Value.SetWebhook(url, secretToken: secretToken, allowedUpdates: allowedUpdates, cancellationToken: ct);

    public async Task SetMyCommandsAsync(IReadOnlyList<BotCommand> commands, CancellationToken ct) =>
        await _client.Value.SetMyCommands(commands, cancellationToken: ct);
}
