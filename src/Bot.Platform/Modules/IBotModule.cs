using Bot.Platform.Messaging;

namespace Bot.Platform.Modules;

public interface IBotModule
{
    /// <summary>^[a-z][a-z0-9]{1,31}$, без "_" (разделитель в start_param).</summary>
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration config);

    /// <summary>Группа уже /api/{Name} и уже RequireAuthorization().</summary>
    void MapApi(IEndpointRouteBuilder api);

    /// <summary>Команды, callback'и и start-payload модуля. Модуль без бота ничего не переопределяет.</summary>
    void MapBot(IBotRouteBuilder bot)
    {
    }
}
