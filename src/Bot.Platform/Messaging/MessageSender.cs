using Bot.Platform.Configuration;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.Extensions.Options;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Messaging;

internal sealed class MessageSender(
    IBotApi api,
    IClock clock,
    IServiceScopeFactory scopeFactory,
    IOptions<BotOptions> options,
    ILogger<MessageSender> logger) : IMessageSender
{
    private const int MaxTextLength = 4096;

    public async Task<SendResult> SendAsync(long chatId, OutgoingMessage message, CancellationToken cancellationToken)
    {
        ValidateText(message);
        cancellationToken.ThrowIfCancellationRequested();

        return await ExecuteAsync(
            chatId,
            editedMessageId: null,
            () => api.SendMessageAsync(chatId, message.Text, message.ReplyMarkup, message.Html, cancellationToken),
            cancellationToken);
    }

    public async Task<SendResult> EditAsync(long chatId, int messageId, OutgoingMessage message, CancellationToken cancellationToken)
    {
        ValidateText(message);
        if (message.ReplyMarkup is not null and not InlineKeyboardMarkup)
        {
            throw new ArgumentException("Only InlineKeyboardMarkup can be used when editing a message.", nameof(message));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await ExecuteAsync(
            chatId,
            editedMessageId: messageId,
            async () =>
            {
                await api.EditMessageTextAsync(
                    chatId,
                    messageId,
                    message.Text,
                    message.ReplyMarkup as InlineKeyboardMarkup,
                    message.Html,
                    cancellationToken);
                return messageId;
            },
            cancellationToken);
    }

    private static void ValidateText(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.Text) || message.Text.Length > MaxTextLength)
        {
            throw new ArgumentException($"Text must be non-blank and at most {MaxTextLength} characters.", nameof(message));
        }
    }

    // Общая политика ошибок Telegram для отправки и редактирования: 403, один повтор при 429, остальное — Failed.
    private async Task<SendResult> ExecuteAsync(long chatId, int? editedMessageId, Func<Task<int>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? wait = null;
            try
            {
                var id = await call();
                return new SendResult(SendStatus.Sent, id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                return await HandleBlockedAsync(chatId, cancellationToken);
            }
            catch (ApiRequestException ex) when (editedMessageId is not null && ex.ErrorCode == 400 && IsNotModified(ex))
            {
                // Повторное нажатие той же кнопки: сообщение уже в нужном виде — это успех.
                logger.LogDebug("Edit in chat {ChatId} skipped: message is not modified", chatId);
                return new SendResult(SendStatus.Sent, editedMessageId);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var retryAfter = ex.Parameters?.RetryAfter;
                if (attempt > 0 || retryAfter is null || TimeSpan.FromSeconds(retryAfter.Value) > options.Value.MaxRetryAfter)
                {
                    logger.LogWarning("Telegram rate limit for chat {ChatId} (retry_after={RetryAfter})", chatId, retryAfter);
                    return new SendResult(SendStatus.RateLimited, null);
                }

                wait = TimeSpan.FromSeconds(retryAfter.Value);
            }
            catch (Exception ex)
            {
                logger.LogWarning("{Operation} chat {ChatId} failed: {ErrorType}", editedMessageId is not null ? "Editing message in" : "Sending to", chatId, ex.GetType().Name);
                return new SendResult(SendStatus.Failed, null);
            }

            await clock.DelayAsync(wait.Value, cancellationToken);
        }
    }

    private static bool IsNotModified(ApiRequestException ex) =>
        ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase);

    private async Task<SendResult> HandleBlockedAsync(long chatId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Chat {ChatId} blocked the bot", chatId);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IUserRegistry>();
            await registry.MarkCannotReceiveMessagesAsync(chatId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to mark user {ChatId} as unreachable", chatId);
        }

        return new SendResult(SendStatus.Blocked, null);
    }
}
