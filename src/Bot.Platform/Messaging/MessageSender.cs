using Bot.Platform.Configuration;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.Extensions.Options;
using Telegram.Bot.Exceptions;

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
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.Text) || message.Text.Length > MaxTextLength)
        {
            throw new ArgumentException($"Text must be non-blank and at most {MaxTextLength} characters.", nameof(message));
        }

        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? wait = null;
            try
            {
                var id = await api.SendMessageAsync(chatId, message.Text, message.ReplyMarkup, message.Html, cancellationToken);
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
                logger.LogWarning("Sending to chat {ChatId} failed: {ErrorType}", chatId, ex.GetType().Name);
                return new SendResult(SendStatus.Failed, null);
            }

            await clock.DelayAsync(wait.Value, cancellationToken);
        }
    }

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
