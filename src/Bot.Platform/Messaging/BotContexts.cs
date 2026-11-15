using Bot.Platform.Auth;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bot.Platform.Messaging;

public delegate Task BotHandler<in TContext>(TContext context)
    where TContext : BotContext;

public abstract class BotContext
{
    public required Update Update { get; init; }

    public required long ChatId { get; init; }

    public required TelegramUser From { get; init; }

    public required IServiceProvider Services { get; init; }

    public required IMessageSender Sender { get; init; }

    public required CancellationToken CancellationToken { get; init; }

    public Task<SendResult> ReplyAsync(OutgoingMessage message) =>
        Sender.SendAsync(ChatId, message, CancellationToken);
}

public sealed class BotCommandContext : BotContext
{
    public required Message Message { get; init; }

    /// <summary>Без "/", без "@bot", lower-case.</summary>
    public required string Command { get; init; }

    /// <summary>Trim по краям, может быть пустой.</summary>
    public required string Arguments { get; init; }
}

public sealed class BotStartContext : BotContext
{
    public required Message Message { get; init; }

    public required string Payload { get; init; }
}

public sealed class BotCallbackContext : BotContext
{
    private int _answered;

    public required CallbackQuery Query { get; init; }

    public required string Action { get; init; }

    public required string Payload { get; init; }

    internal IBotApi? Api { get; init; }

    internal ILogger? Logger { get; init; }

    /// <summary>Повторный вызов — no-op; ошибки Telegram не бросает.</summary>
    public Task AnswerAsync(string? text = null, bool showAlert = false) => AnswerOnceAsync(text, showAlert);

    internal async Task AnswerOnceAsync(string? text, bool showAlert)
    {
        if (Interlocked.Exchange(ref _answered, 1) == 1 || Api is null)
        {
            return;
        }

        try
        {
            await Api.AnswerCallbackQueryAsync(Query.Id, text, showAlert, CancellationToken);
        }
        catch (Exception ex) when (ex is ApiRequestException or RequestException or HttpRequestException)
        {
            Logger?.LogWarning("answerCallbackQuery failed: {ErrorType}", ex.GetType().Name);
        }
    }
}
