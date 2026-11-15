using Bot.Platform.Messaging;
using Bot.Platform.Modules;

namespace Bot.Platform.Tests.Helpers;

/// <summary>Modules with deliberately (in)valid names for AC-3.</summary>
public abstract class NamedModule(string name) : IBotModule
{
    public string Name { get; } = name;

    public void AddServices(IServiceCollection services, IConfiguration config)
    {
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }
}

public sealed class EmptyNameModule() : NamedModule("");
public sealed class UppercaseNameModule() : NamedModule("Demo");
public sealed class UnderscoreNameModule() : NamedModule("demo_x");
public sealed class DigitFirstNameModule() : NamedModule("1demo");
public sealed class OneCharNameModule() : NamedModule("a");
public sealed class TooLongNameModule() : NamedModule("abcdefghijklmnopqrstuvwxyzabcdefg");      // 33
public sealed class MaxLengthNameModule() : NamedModule("abcdefghijklmnopqrstuvwxyzabcdef");     // 32 (valid)
public sealed class MinLengthNameModule() : NamedModule("ab");                                    // valid
public sealed class ReservedPlatformModule() : NamedModule("platform");
public sealed class ReservedBotModule() : NamedModule("bot");
public sealed class ReservedHealthModule() : NamedModule("health");
public sealed class ReservedAssetsModule() : NamedModule("assets");

/// <summary>Second module "demob" with one command (AC-6: registered after "demo").</summary>
public sealed class DemoBModule : IBotModule
{
    public string Name => "demob";

    public void AddServices(IServiceCollection services, IConfiguration config)
    {
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapBot(IBotRouteBuilder bot) =>
        bot.MapCommand("zeta", "Последняя", _ => Task.CompletedTask);
}

/// <summary>Module with commands but without a start handler / callbacks ("/start demob_x" falls back to the greeting).</summary>
public sealed class NoMapBotModule : NamedModule
{
    public NoMapBotModule() : base("plain")
    {
    }
}

/// <summary>
/// Module whose routes are provided by the test (AC-12 invalid routes). The mapping is taken from an AsyncLocal set by
/// <see cref="Use"/> before <c>AddModule&lt;ConfigurableModule&gt;()</c>; <see cref="ConfigurableModuleB"/> is the same with an
/// independent slot (two modules in one host).
/// </summary>
public sealed class ConfigurableModule : IBotModule
{
    private static readonly AsyncLocal<(string Name, Action<IBotRouteBuilder> Map)?> Current = new();

    public static IDisposable Use(string name, Action<IBotRouteBuilder> map)
    {
        Current.Value = (name, map);
        return new Reset(() => Current.Value = null);
    }

    private readonly (string Name, Action<IBotRouteBuilder> Map) _config =
        Current.Value ?? throw new InvalidOperationException("ConfigurableModule.Use was not called");

    public string Name => _config.Name;

    public void AddServices(IServiceCollection services, IConfiguration config)
    {
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapBot(IBotRouteBuilder bot) => _config.Map(bot);

    private sealed class Reset(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

public sealed class ConfigurableModuleB : IBotModule
{
    private static readonly AsyncLocal<(string Name, Action<IBotRouteBuilder> Map)?> Current = new();

    public static IDisposable Use(string name, Action<IBotRouteBuilder> map)
    {
        Current.Value = (name, map);
        return new Reset(() => Current.Value = null);
    }

    private readonly (string Name, Action<IBotRouteBuilder> Map) _config =
        Current.Value ?? throw new InvalidOperationException("ConfigurableModuleB.Use was not called");

    public string Name => _config.Name;

    public void AddServices(IServiceCollection services, IConfiguration config)
    {
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapBot(IBotRouteBuilder bot) => _config.Map(bot);

    private sealed class Reset(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
