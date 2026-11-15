namespace Bot.Platform.Messaging;

public interface IBotRouteBuilder
{
    string ModuleName { get; }

    IBotRouteBuilder MapCommand(string command, string description, BotHandler<BotCommandContext> handler);

    /// <summary>data = "{module}:{action}[:{payload}]".</summary>
    IBotRouteBuilder MapCallback(string action, BotHandler<BotCallbackContext> handler);

    /// <summary>"/start {module}_{payload}".</summary>
    IBotRouteBuilder MapStart(BotHandler<BotStartContext> handler);

    /// <summary>Обработчик текстового ответа пользователя после <see cref="IBotConversation.ExpectReplyAsync"/>.</summary>
    IBotRouteBuilder MapReply(string action, BotHandler<BotReplyContext> handler);
}

internal sealed record CommandRoute(string Module, string Command, string Description, BotHandler<BotCommandContext> Handler);

internal sealed class BotRouteTable
{
    public const int MaxCommands = 100;
    private const int PlatformCommands = 2;

    private readonly List<CommandRoute> _commands = [];
    private readonly Dictionary<string, CommandRoute> _commandsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Module, string Action), BotHandler<BotCallbackContext>> _callbacks = [];
    private readonly Dictionary<(string Module, string Action), BotHandler<BotReplyContext>> _replies = [];
    private readonly Dictionary<string, BotHandler<BotStartContext>> _starts = new(StringComparer.Ordinal);

    public IReadOnlyList<CommandRoute> Commands => _commands;

    public static BotRouteTable GetOrAdd(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(d => d.ServiceType == typeof(BotRouteTable))
            ?.ImplementationInstance as BotRouteTable;
        if (existing is not null)
        {
            return existing;
        }

        var table = new BotRouteTable();
        services.AddSingleton(table);
        return table;
    }

    public IBotRouteBuilder CreateBuilder(string moduleName) => new ModuleRouteBuilder(this, moduleName);

    public CommandRoute? FindCommand(string command) => _commandsByName.GetValueOrDefault(command);

    public BotHandler<BotCallbackContext>? FindCallback(string module, string action) =>
        _callbacks.GetValueOrDefault((module, action));

    public BotHandler<BotReplyContext>? FindReply(string module, string action) =>
        _replies.GetValueOrDefault((module, action));

    public BotHandler<BotStartContext>? FindStart(string module) => _starts.GetValueOrDefault(module);

    private sealed class ModuleRouteBuilder(BotRouteTable table, string moduleName) : IBotRouteBuilder
    {
        public string ModuleName => moduleName;

        public IBotRouteBuilder MapCommand(string command, string description, BotHandler<BotCommandContext> handler)
        {
            var label = $"command '{command}'";
            if (handler is null)
            {
                throw Error(label, "handler must not be null");
            }

            if (command is null || !BotNames.IsValidName(command))
            {
                throw Error(label, "command must match ^[a-z0-9_]{1,32}$");
            }

            if (command is "start" or "help")
            {
                throw Error(label, "command is reserved by the platform");
            }

            var trimmed = description?.Trim() ?? string.Empty;
            if (trimmed.Length is < 3 or > 256)
            {
                throw Error(label, "description must be 3-256 characters");
            }

            if (table._commandsByName.TryGetValue(command, out var taken))
            {
                throw Error(
                    label,
                    taken.Module == moduleName
                        ? "command is already registered in this module"
                        : $"command is already registered by module '{taken.Module}'");
            }

            if (table._commands.Count + PlatformCommands >= MaxCommands)
            {
                throw Error(label, $"total number of commands must not exceed {MaxCommands}");
            }

            var route = new CommandRoute(moduleName, command, trimmed, handler);
            table._commands.Add(route);
            table._commandsByName[command] = route;
            return this;
        }

        public IBotRouteBuilder MapCallback(string action, BotHandler<BotCallbackContext> handler)
        {
            var label = $"callback '{action}'";
            if (handler is null)
            {
                throw Error(label, "handler must not be null");
            }

            if (action is null || !BotNames.IsValidName(action))
            {
                throw Error(label, "action must match ^[a-z0-9_]{1,32}$");
            }

            if (!table._callbacks.TryAdd((moduleName, action), handler))
            {
                throw Error(label, "action is already registered in this module");
            }

            return this;
        }

        public IBotRouteBuilder MapReply(string action, BotHandler<BotReplyContext> handler)
        {
            var label = $"reply '{action}'";
            if (handler is null)
            {
                throw Error(label, "handler must not be null");
            }

            if (action is null || !BotNames.IsValidName(action))
            {
                throw Error(label, "action must match ^[a-z0-9_]{1,32}$");
            }

            if (!table._replies.TryAdd((moduleName, action), handler))
            {
                throw Error(label, "action is already registered in this module");
            }

            return this;
        }

        public IBotRouteBuilder MapStart(BotHandler<BotStartContext> handler)
        {
            const string label = "start handler";
            if (handler is null)
            {
                throw Error(label, "handler must not be null");
            }

            if (!table._starts.TryAdd(moduleName, handler))
            {
                throw Error(label, "start handler is already registered in this module");
            }

            return this;
        }

        private InvalidOperationException Error(string what, string reason) =>
            new($"Cannot register bot route for module '{moduleName}', {what}: {reason}.");
    }
}
