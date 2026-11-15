using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bot.Platform.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bot.Platform.Messaging;

internal static class WebhookEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(BotDefaults.WebhookPath, HandleAsync).AllowAnonymous();

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IOptions<BotOptions> options,
        IUpdateDispatcher dispatcher,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Bot.Platform.Messaging.Webhook");
        var ct = context.RequestAborted;

        var header = context.Request.Headers[BotDefaults.SecretHeader];
        if (header.Count != 1
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(header[0] ?? string.Empty),
                Encoding.UTF8.GetBytes(options.Value.WebhookSecret)))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(context.Request.Body, JsonBotAPI.Options, ct);
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (update is null)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        try
        {
            await dispatcher.DispatchAsync(update, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Telegram оборвал соединение — обработка отменена, ответ уже не нужен.
        }
        catch (Exception ex)
        {
            // Не-200 заставил бы Telegram повторять обновление бесконечно.
            logger.LogError(ex, "Update {UpdateId} handling failed", update.Id);
        }

        return Results.Ok();
    }
}
