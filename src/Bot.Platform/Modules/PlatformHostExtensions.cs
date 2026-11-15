using System.Text.RegularExpressions;
using Bot.Platform.Api;
using Bot.Platform.Auth;
using Bot.Platform.Configuration;
using Bot.Platform.Messaging;
using Bot.Platform.Persistence;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Bot.Platform.Modules;

public static partial class PlatformHostExtensions
{
    private const string DefaultDistPath = "../../webapp/dist";

    public static WebApplicationBuilder AddPlatform(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var config = builder.Configuration;

        ModuleRegistry.GetOrAdd(services);
        services.AddSingleton<IClock, UtcClock>();

        services.AddOptions<BotOptions>()
            .Bind(config.GetSection(BotOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Configuration key 'Bot:Token' is required.")
            .Validate(
                o => !string.IsNullOrEmpty(o.WebhookSecret) && WebhookSecretPattern().IsMatch(o.WebhookSecret),
                "Configuration key 'Bot:WebhookSecret' is required and must match ^[A-Za-z0-9_-]{1,256}$.")
            .Validate(
                o => o.PublicUrl is null || o.PublicUrl is { IsAbsoluteUri: true, Scheme: "https", Query: "", Fragment: "" },
                "Configuration key 'Bot:PublicUrl' must be an absolute https URL without query or fragment.")
            .Validate(
                o => o.MaxRetryAfter >= TimeSpan.Zero && o.MaxRetryAfter <= TimeSpan.FromMinutes(1),
                "Configuration key 'Bot:MaxRetryAfter' must be between 00:00:00 and 00:01:00.")
            .Validate(
                o => o.Username is null || UsernamePattern().IsMatch(o.Username),
                "Configuration key 'Bot:Username' must match ^[A-Za-z][A-Za-z0-9_]{4,31}$ (without '@').")
            .Validate(
                o => o.MiniAppShortName is null || MiniAppShortNamePattern().IsMatch(o.MiniAppShortName),
                "Configuration key 'Bot:MiniAppShortName' must match ^[A-Za-z0-9_]{3,30}$.")
            .Validate(
                o => o.ReplyTimeout >= TimeSpan.FromMinutes(1) && o.ReplyTimeout <= TimeSpan.FromDays(1),
                "Configuration key 'Bot:ReplyTimeout' must be between 00:01:00 and 1.00:00:00.")
            .ValidateOnStart();
        services.AddOptions<AuthOptions>()
            .Bind(config.GetSection(AuthOptions.SectionName))
            .Validate(o => o.InitDataMaxAge > TimeSpan.Zero, "Configuration key 'Auth:InitDataMaxAge' must be positive.")
            .Validate(o => o.AllowedClockSkew >= TimeSpan.Zero, "Configuration key 'Auth:AllowedClockSkew' must not be negative.")
            .ValidateOnStart();

        // У клиента "telegram" логгеры сняты: URL запросов Bot API содержит токен бота.
        // SocketsHttpHandler с PooledConnectionLifetime — клиент живёт в singleton IBotApi, нужна ротация DNS.
        services.AddHttpClient(BotDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .RemoveAllLoggers();
        BotRouteTable.GetOrAdd(services);
        services.AddSingleton<IBotApi, TelegramBotApi>();
        services.AddSingleton<IMessageSender, MessageSender>();
        services.AddSingleton<IDeepLinks, DeepLinks>();
        services.AddSingleton<IUpdateDispatcher, UpdateDispatcher>();
        services.AddHostedService<BotSetupService>();

        services.AddSingleton<IInitDataValidator, InitDataValidator>();
        services.AddAuthentication(TelegramInitDataDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, TelegramInitDataHandler>(TelegramInitDataDefaults.Scheme, null);
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(TelegramInitDataDefaults.Scheme)
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddModuleDbContext<PlatformDbContext>(config, "platform");
        services.AddScoped<BotConversation>();
        services.AddScoped<IBotConversation>(sp => sp.GetRequiredService<BotConversation>());
        services.AddScoped<UserStore>();
        services.AddScoped<IUserDirectory>(sp => sp.GetRequiredService<UserStore>());
        services.AddScoped<IUserRegistry>(sp => sp.GetRequiredService<UserStore>());
        return builder;
    }

    public static WebApplicationBuilder AddModule<TModule>(this WebApplicationBuilder builder)
        where TModule : class, IBotModule, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        var module = new TModule();
        ModuleRegistry.GetOrAdd(builder.Services).Add(module);
        module.AddServices(builder.Services, builder.Configuration);
        IBotModule botModule = module;
        botModule.MapBot(BotRouteTable.GetOrAdd(builder.Services).CreateBuilder(module.Name));
        return builder;
    }

    public static WebApplication MapPlatform(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler(errorApp => errorApp.Run(context =>
            ApiErrors.Problem(StatusCodes.Status500InternalServerError, "internal_error", "Internal server error")
                .ExecuteAsync(context)));

        var distPath = ResolveDistPath(app);
        var hasDist = Directory.Exists(distPath);
        if (hasDist)
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(distPath),
                OnPrepareResponse = ctx =>
                {
                    var immutable = ctx.Context.Request.Path.StartsWithSegments("/assets");
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] =
                        immutable ? "public, max-age=31536000, immutable" : "no-cache";
                },
            });
        }
        else
        {
            app.Logger.LogWarning("WebApp dist folder not found at '{DistPath}'; Mini App will not be served.", distPath);
        }

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        WebhookEndpoint.Map(app);

        app.MapGet("/health", () => new HealthDto("ok"));

        var api = app.MapGroup("/api").RequireAuthorization();

        var platform = api.MapGroup("/platform");
        platform.MapGet("/me", async (HttpContext context, IUserRegistry registry, CancellationToken cancellationToken) =>
        {
            var user = context.GetInitData().User;
            await registry.UpsertAsync(user, user.AllowsWriteToPm == true ? true : null, cancellationToken);
            return new MeDto(user.Id, user.FirstName, user.LastName, user.Username, user.LanguageCode);
        });

        foreach (var module in app.Services.GetRequiredService<ModuleRegistry>().Modules)
        {
            module.MapApi(api.MapGroup($"/{module.Name}"));
        }

        api.MapFallback("{**path}", () => ApiErrors.NotFound());

        var indexPath = Path.Combine(distPath, "index.html");
        app.MapFallback(context =>
        {
            var path = context.Request.Path;
            var reserved = path.StartsWithSegments("/api") || path.StartsWithSegments("/bot")
                || path.StartsWithSegments("/health") || path.StartsWithSegments("/assets");
            if (reserved || !hasDist || !File.Exists(indexPath) || !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
            context.Response.ContentType = "text/html; charset=utf-8";
            return context.Response.SendFileAsync(indexPath, context.RequestAborted);
        });

        return app;
    }

    private static string ResolveDistPath(WebApplication app)
    {
        var configured = app.Configuration["WebApp:DistPath"];
        var path = string.IsNullOrWhiteSpace(configured) ? DefaultDistPath : configured;
        return Path.GetFullPath(path, app.Environment.ContentRootPath);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{4,31}\\z")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[A-Za-z0-9_]{3,30}\\z")]
    private static partial Regex MiniAppShortNamePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,256}\\z")]
    private static partial Regex WebhookSecretPattern();
}
