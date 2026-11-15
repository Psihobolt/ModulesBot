using System.Net;
using System.Text;
using Bot.Platform.Messaging;
using NSubstitute;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Platform.Tests.Helpers;

/// <summary>Shared constants and factories for the bot tests (spec 002, TEST-1).</summary>
public static class TestBot
{
    public const string WebhookSecret = "test-webhook-secret";
    public const string PublicUrl = "https://example.org";

    public const string Welcome =
        "Привет! Это бот с мини-приложением.\nНажмите «Открыть приложение», чтобы начать. Список команд — /help";

    public const string UnknownCommand = "Не знаю такую команду. Список команд — /help";
    public const string NotACommand = "Я понимаю только команды. Список команд — /help";
    public const string SendTextOnly = "Отправьте ответ текстом.";
    public const string StaleButton = "Эта кнопка больше не работает";

    public const string HelpBase = "Команды:\n/start — открыть приложение\n/help — список команд";

    public static ApiRequestException Forbidden() =>
        new("Forbidden: bot was blocked by the user", 403);

    public static ApiRequestException BadRequest(string description = "Bad Request: query is too old") =>
        new(description, 400);

    /// <summary>429; <paramref name="retryAfter"/> = null means "no parameters at all".</summary>
    public static ApiRequestException TooManyRequests(int? retryAfter) =>
        retryAfter is null
            ? new ApiRequestException("Too Many Requests", 429)
            : new ApiRequestException("Too Many Requests: retry after " + retryAfter, 429,
                new ResponseParameters { RetryAfter = retryAfter });
}

/// <summary>Programmable fake HTTP layer for the "telegram" HttpClient (AC-23): replaces the primary handler.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly object _gate = new();

    public List<(Uri Uri, string Body)> Requests { get; } = [];

    public int CallCount
    {
        get { lock (_gate) return Requests.Count; }
    }

    /// <summary>Response used when the queue is empty.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Default { get; set; } = _ => Json(HttpStatusCode.OK, OkMessage(7));

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string json)
    {
        _responses.Enqueue(_ => Json(status, json));
        return this;
    }

    public static string OkMessage(int messageId) =>
        "{\"ok\":true,\"result\":{\"message_id\":" + messageId +
        ",\"date\":1700000000,\"chat\":{\"id\":42,\"type\":\"private\",\"first_name\":\"T\"},\"text\":\"x\"}}";

    public static string ApiError(int code, string description, string? parametersJson = null) =>
        "{\"ok\":false,\"error_code\":" + code + ",\"description\":\"" + description + "\"" +
        (parametersJson is null ? "" : ",\"parameters\":" + parametersJson) + "}";

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpRequestMessage, HttpResponseMessage> responder;
        lock (_gate)
        {
            Requests.Add((request.RequestUri!, body));
            responder = _responses.Count > 0 ? _responses.Dequeue() : Default;
        }

        return responder(request);
    }
}

/// <summary>Substitute IBotApi (NSubstitute) + convenient views over recorded calls.</summary>
internal static class BotApiMock
{
    public sealed record SentMessage(long ChatId, string Text, ReplyMarkup? ReplyMarkup, bool Html);

    public sealed record EditedMessage(long ChatId, int MessageId, string Text, InlineKeyboardMarkup? ReplyMarkup, bool Html);

    public sealed record AnsweredCallback(string QueryId, string? Text, bool ShowAlert);

    /// <summary>SendMessageAsync returns 1, 2, 3, ...; other methods complete successfully.</summary>
    public static IBotApi Create()
    {
        var api = Substitute.For<IBotApi>();
        var next = 0;
        api.SendMessageAsync(default, default!, default, default, default)
            .ReturnsForAnyArgs(_ => Task.FromResult(Interlocked.Increment(ref next)));
        api.AnswerCallbackQueryAsync(default!, default, default, default).ReturnsForAnyArgs(Task.CompletedTask);
        api.EditMessageTextAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(Task.CompletedTask);
        api.SetWebhookAsync(default!, default!, default!, default).ReturnsForAnyArgs(Task.CompletedTask);
        api.SetMyCommandsAsync(default!, default).ReturnsForAnyArgs(Task.CompletedTask);
        return api;
    }

    public static IReadOnlyList<SentMessage> Sent(this IBotApi api) =>
        api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBotApi.SendMessageAsync))
            .Select(c => c.GetArguments())
            .Select(a => new SentMessage((long)a[0]!, (string)a[1]!, (ReplyMarkup?)a[2], (bool)a[3]!))
            .ToList();

    public static IReadOnlyList<EditedMessage> Edits(this IBotApi api) =>
        api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBotApi.EditMessageTextAsync))
            .Select(c => c.GetArguments())
            .Select(a => new EditedMessage((long)a[0]!, (int)a[1]!, (string)a[2]!, (InlineKeyboardMarkup?)a[3], (bool)a[4]!))
            .ToList();

    public static IReadOnlyList<AnsweredCallback> Answers(this IBotApi api) =>
        api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBotApi.AnswerCallbackQueryAsync))
            .Select(c => c.GetArguments())
            .Select(a => new AnsweredCallback((string)a[0]!, (string?)a[1], (bool)a[2]!))
            .ToList();

    public static int CallCount(this IBotApi api) => api.ReceivedCalls().Count();
}
