using Bot.Platform.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot.Platform.Messaging;

internal sealed class BotSetupService(
    IBotApi api,
    BotRouteTable routes,
    IOptions<BotOptions> options,
    ILogger<BotSetupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (o.PublicUrl is null || !o.SetupOnStartup)
        {
            return;
        }

        var url = o.PublicUrl.OriginalString.TrimEnd('/') + BotDefaults.WebhookPath;
        try
        {
            await api.SetWebhookAsync(url, o.WebhookSecret, [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("SetWebhook failed: {ErrorType}", ex.GetType().Name);
        }

        try
        {
            var commands = new List<BotCommand>
            {
                new() { Command = "start", Description = "открыть приложение" },
                new() { Command = "help", Description = "список команд" },
            };
            commands.AddRange(routes.Commands.Select(c => new BotCommand { Command = c.Command, Description = c.Description }));
            await api.SetMyCommandsAsync(commands, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("SetMyCommands failed: {ErrorType}", ex.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
