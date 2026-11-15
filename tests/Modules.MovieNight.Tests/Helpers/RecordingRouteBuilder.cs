using Bot.Platform.Messaging;

namespace Modules.MovieNight.Tests.Helpers;

/// <summary>Публичный IBotRouteBuilder: запоминает регистрации модуля без DI и без вызова обработчиков.</summary>
public sealed class RecordingRouteBuilder(string moduleName = "movienight") : IBotRouteBuilder
{
    public List<(string Command, string Description)> Commands { get; } = [];

    public List<string> Callbacks { get; } = [];

    public List<string> Replies { get; } = [];

    public int Starts { get; private set; }

    public string ModuleName => moduleName;

    public IBotRouteBuilder MapCommand(string command, string description, BotHandler<BotCommandContext> handler)
    {
        Commands.Add((command, description));
        return this;
    }

    public IBotRouteBuilder MapCallback(string action, BotHandler<BotCallbackContext> handler)
    {
        Callbacks.Add(action);
        return this;
    }

    public IBotRouteBuilder MapStart(BotHandler<BotStartContext> handler)
    {
        Starts++;
        return this;
    }

    public IBotRouteBuilder MapReply(string action, BotHandler<BotReplyContext> handler)
    {
        Replies.Add(action);
        return this;
    }
}
