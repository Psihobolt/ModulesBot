using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Messaging;

/// <summary>Единственная обёртка над ITelegramBotClient. Исключения Telegram.Bot пробрасываются как есть.</summary>
internal interface IBotApi
{
    /// <returns>messageId</returns>
    Task<int> SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup, bool html, CancellationToken ct);

    Task EditMessageTextAsync(long chatId, int messageId, string text, InlineKeyboardMarkup? replyMarkup, bool html, CancellationToken ct);

    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, bool showAlert, CancellationToken ct);

    Task SetWebhookAsync(string url, string secretToken, IReadOnlyCollection<UpdateType> allowedUpdates, CancellationToken ct);

    Task SetMyCommandsAsync(IReadOnlyList<BotCommand> commands, CancellationToken ct);
}
