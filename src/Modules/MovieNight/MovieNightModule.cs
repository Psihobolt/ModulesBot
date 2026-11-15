using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Bot.Platform.Persistence;
using Modules.MovieNight.Api;
using Modules.MovieNight.Application;
using Modules.MovieNight.Persistence;
using Modules.MovieNight.Telegram;

namespace Modules.MovieNight;

public sealed class MovieNightModule : IBotModule
{
    public string Name => "movienight";

    public void AddServices(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<MovieNightDbContext>(config, Name);
        services.AddScoped<IGroupService, EfGroupService>();
        services.AddSingleton<IInviteCodeGenerator, InviteCodeGenerator>();
    }

    public void MapApi(IEndpointRouteBuilder api) => GroupEndpoints.Map(api);

    public void MapBot(IBotRouteBuilder bot) => GroupBotRoutes.Map(bot);
}
