using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Messaging;

public sealed record OutgoingMessage(string Text, ReplyMarkup? ReplyMarkup = null, bool Html = false);

public enum SendStatus
{
    Sent,
    Blocked,
    RateLimited,
    Failed,
}

public sealed record SendResult(SendStatus Status, int? MessageId);

public interface IMessageSender
{
    Task<SendResult> SendAsync(long chatId, OutgoingMessage message, CancellationToken cancellationToken);

    /// <summary>Редактирует текст и inline-клавиатуру сообщения. ReplyMarkup — только InlineKeyboardMarkup или null.</summary>
    Task<SendResult> EditAsync(long chatId, int messageId, OutgoingMessage message, CancellationToken cancellationToken);
}
