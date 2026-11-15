using System.Text.RegularExpressions;
using Bot.Platform.Auth;
using Bot.Platform.Configuration;
using Bot.Platform.Users;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgUser = Telegram.Bot.Types.User;

namespace Bot.Platform.Messaging;

internal interface IUpdateDispatcher
{
    Task DispatchAsync(Update update, CancellationToken cancellationToken);
}

internal sealed partial class UpdateDispatcher(
    IServiceScopeFactory scopeFactory,
    BotRouteTable routes,
    IBotApi api,
    IMessageSender sender,
    IOptions<BotOptions> options,
    ILogger<UpdateDispatcher> logger) : IUpdateDispatcher
{
    private const int MaxStartPayloadLength = 64;

    public async Task DispatchAsync(Update update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        logger.LogDebug("Update {UpdateId} of type {UpdateType}", update.Id, update.Type);

        switch (update.Type)
        {
            case UpdateType.Message when update.Message is { From: { } from } message
                && message.Chat.Type == ChatType.Private:
                await HandleMessageAsync(update, message, from, cancellationToken);
                break;
            case UpdateType.CallbackQuery when update.CallbackQuery is { Message: { } cbMessage } query
                && cbMessage.Chat.Type == ChatType.Private:
                await HandleCallbackAsync(update, query, cbMessage.Chat.Id, cancellationToken);
                break;
            default:
                logger.LogDebug("Update {UpdateId} ignored", update.Id);
                break;
        }
    }

    private async Task HandleMessageAsync(Update update, Message message, TgUser from, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var user = ToUser(from);
        await scope.ServiceProvider.GetRequiredService<IUserRegistry>().UpsertAsync(user, true, ct);

        var chatId = message.Chat.Id;
        var text = message.Text;
        var conversation = scope.ServiceProvider.GetRequiredService<BotConversation>();
        if (string.IsNullOrWhiteSpace(text))
        {
            // Нет текста или только пробелы: ожидание не потребляется (AC-7a).
            var waiting = await conversation.HasActiveAsync(user.Id, ct);
            await ReplyAsync(chatId, waiting ? Texts.TextOnly : Texts.NotACommand, ct);
            return;
        }

        if (!text.StartsWith('/'))
        {
            await HandleTextAsync(update, message, user, scope.ServiceProvider, conversation, text, ct);
            return;
        }

        // Любая команда сбрасывает ожидание ответа.
        await conversation.CancelAsync(user.Id, ct);

        var match = CommandPattern().Match(text);
        if (!match.Success)
        {
            await ReplyAsync(chatId, Texts.UnknownCommand, ct);
            return;
        }

        var command = match.Groups[1].Value.ToLowerInvariant();
        var arguments = match.Groups[4].Value.Trim();

        switch (command)
        {
            case "start":
                await HandleStartAsync(update, message, user, scope.ServiceProvider, arguments, ct);
                return;
            case "help":
                await ReplyAsync(chatId, BuildHelp(), ct);
                return;
        }

        var route = routes.FindCommand(command);
        if (route is null)
        {
            await ReplyAsync(chatId, Texts.UnknownCommand, ct);
            return;
        }

        await route.Handler(new BotCommandContext
        {
            Update = update,
            ChatId = chatId,
            From = user,
            Services = scope.ServiceProvider,
            Sender = sender,
            CancellationToken = ct,
            Message = message,
            Command = command,
            Arguments = arguments,
        });
    }

    private async Task HandleTextAsync(
        Update update,
        Message message,
        TelegramUser user,
        IServiceProvider services,
        BotConversation conversation,
        string text,
        CancellationToken ct)
    {
        var pending = await conversation.ConsumeAsync(user.Id, ct);
        var handler = pending is null ? null : routes.FindReply(pending.Module, pending.Action);
        if (pending is null || handler is null)
        {
            await ReplyAsync(message.Chat.Id, Texts.NotACommand, ct);
            return;
        }

        await handler(new BotReplyContext
        {
            Update = update,
            ChatId = message.Chat.Id,
            From = user,
            Services = services,
            Sender = sender,
            CancellationToken = ct,
            Message = message,
            Action = pending.Action,
            Payload = pending.Payload,
            Text = text.Trim(),
        });
    }

    private async Task HandleStartAsync(
        Update update,
        Message message,
        TelegramUser user,
        IServiceProvider services,
        string arguments,
        CancellationToken ct)
    {
        var payloadMatch = StartPayloadPattern().Match(arguments);
        if (arguments.Length <= MaxStartPayloadLength && payloadMatch.Success
            && routes.FindStart(payloadMatch.Groups[1].Value) is { } handler)
        {
            await handler(new BotStartContext
            {
                Update = update,
                ChatId = message.Chat.Id,
                From = user,
                Services = services,
                Sender = sender,
                CancellationToken = ct,
                Message = message,
                Payload = payloadMatch.Groups[2].Value,
            });
            return;
        }

        ReplyMarkup? markup = null;
        if (options.Value.PublicUrl is { } url)
        {
            markup = new InlineKeyboardMarkup(
                InlineKeyboardButton.WithWebApp(Texts.OpenApp, new WebAppInfo(url.OriginalString)));
        }

        await ReplyAsync(message.Chat.Id, Texts.Welcome, ct, markup);
    }

    private async Task HandleCallbackAsync(Update update, CallbackQuery query, long chatId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var user = ToUser(query.From);

        CallbackData.TryParse(query.Data, out var module, out var action, out var payload);
        var handler = module.Length > 0 ? routes.FindCallback(module, action) : null;

        var context = new BotCallbackContext
        {
            Update = update,
            ChatId = chatId,
            From = user,
            Services = scope.ServiceProvider,
            Sender = sender,
            CancellationToken = ct,
            Query = query,
            Action = action,
            Payload = payload,
            Api = api,
            Logger = logger,
        };

        try
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<IUserRegistry>().UpsertAsync(user, true, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Кнопка не должна «висеть» из-за сбоя БД: ответ на callback уходит в finally.
                logger.LogError(ex, "Upsert of user {UserId} failed while handling callback {UpdateId}", user.Id, update.Id);
                return;
            }

            if (handler is null)
            {
                await context.AnswerOnceAsync(Texts.StaleButton, false);
                return;
            }

            await handler(context);
        }
        finally
        {
            await context.AnswerOnceAsync(null, false);
        }
    }

    private string BuildHelp()
    {
        var lines = new List<string> { "Команды:", "/start — открыть приложение", "/help — список команд" };
        lines.AddRange(routes.Commands.Select(c => $"/{c.Command} — {c.Description}"));
        return string.Join('\n', lines);
    }

    private Task ReplyAsync(long chatId, string text, CancellationToken ct, ReplyMarkup? markup = null) =>
        sender.SendAsync(chatId, new OutgoingMessage(text, markup), ct);

    private static TelegramUser ToUser(TgUser u) => new(
        u.Id,
        u.FirstName,
        u.LastName,
        u.Username,
        u.LanguageCode,
        u.IsPremium,
        true);

    [GeneratedRegex(@"^/([A-Za-z0-9_]{1,32})(@\S+)?(\s+([\s\S]*))?$")]
    private static partial Regex CommandPattern();

    [GeneratedRegex(@"^([a-z][a-z0-9]{1,31})_([A-Za-z0-9_-]+)\z")]
    private static partial Regex StartPayloadPattern();
}

internal static class Texts
{
    public const string Welcome = "Привет! Это бот с мини-приложением.\nНажмите «Открыть приложение», чтобы начать. Список команд — /help";
    public const string OpenApp = "Открыть приложение";
    public const string UnknownCommand = "Не знаю такую команду. Список команд — /help";
    public const string NotACommand = "Я понимаю только команды. Список команд — /help";
    public const string TextOnly = "Отправьте ответ текстом.";
    public const string StaleButton = "Эта кнопка больше не работает";
}
