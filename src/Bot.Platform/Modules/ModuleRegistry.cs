using System.Text.RegularExpressions;

namespace Bot.Platform.Modules;

internal sealed partial class ModuleRegistry
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "platform", "bot", "health", "assets",
    };

    private readonly List<IBotModule> _modules = [];

    public IReadOnlyList<IBotModule> Modules => _modules;

    public static ModuleRegistry GetOrAdd(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(d => d.ServiceType == typeof(ModuleRegistry))
            ?.ImplementationInstance as ModuleRegistry;
        if (existing is not null)
        {
            return existing;
        }

        var registry = new ModuleRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    public void Add(IBotModule module)
    {
        var name = module.Name ?? string.Empty;
        var problem = Validate(name);
        if (problem is not null)
        {
            throw new InvalidOperationException($"Cannot register module '{name}': {problem}.");
        }

        _modules.Add(module);
    }

    private string? Validate(string name)
    {
        if (!NamePattern().IsMatch(name))
        {
            return "name must match ^[a-z][a-z0-9]{1,31}$ (lowercase latin letters and digits, 2-32 chars, no '_')";
        }

        if (Reserved.Contains(name))
        {
            return "name is reserved by the platform";
        }

        if (_modules.Any(m => m.Name == name))
        {
            return "a module with this name is already registered";
        }

        return null;
    }

    [GeneratedRegex("^[a-z][a-z0-9]{1,31}$")]
    private static partial Regex NamePattern();
}
