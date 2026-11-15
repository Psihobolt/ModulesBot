using Bot.Platform.Api;
using Bot.Platform.Auth;
using Bot.Platform.Messaging;
using Bot.Platform.Modules;

namespace Bot.Platform.Tests.Helpers;

public sealed class PingCounter
{
    public int Count;
}

public sealed class DemoMarker
{
    public string Value => "demo-marker";
}

/// <summary>Records everything the demo bot handlers received (thread-safe).</summary>
public sealed class BotRecorder
{
    public System.Collections.Concurrent.ConcurrentQueue<BotCommandContext> Commands { get; } = new();

    public System.Collections.Concurrent.ConcurrentQueue<BotCallbackContext> Callbacks { get; } = new();

    public System.Collections.Concurrent.ConcurrentQueue<BotStartContext> Starts { get; } = new();

    /// <summary>Text replies (003 MapReply) received by demo reply handlers.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<BotReplyContext> Replies { get; } = new();

    /// <summary>"/list gate" blocks until this is completed (parallel-updates test).</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource GateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int TotalHandlerCalls => Commands.Count + Callbacks.Count + Starts.Count;
}

/// <summary>Test-only module "demo" (spec AC-1). Lives only in tests.</summary>
public sealed class DemoModule : IBotModule
{
    public string Name => "demo";

    public void AddServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<PingCounter>();
        services.AddSingleton<DemoMarker>();
        services.AddSingleton<BotRecorder>();
    }

    /// <summary>
    /// Commands: ping "Проверка связи", list "Список" (this order; spec AC-6). Callbacks: act, answer, twice, boom.
    /// Bomb: "/ping boom", callback "demo:boom", "/start demo_boom" throw. "/list gate" waits for BotRecorder.Gate.
    /// </summary>
    public void MapBot(IBotRouteBuilder bot)
    {
        bot.MapCommand("ping", "Проверка связи", ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Commands.Enqueue(ctx);
            if (ctx.Arguments == "boom") throw new InvalidOperationException("secret-boom-detail");
            return Task.CompletedTask;
        });
        bot.MapCommand("list", "Список", async ctx =>
        {
            var rec = ctx.Services.GetRequiredService<BotRecorder>();
            rec.Commands.Enqueue(ctx);
            if (ctx.Arguments == "gate")
            {
                rec.GateEntered.TrySetResult();
                await rec.Gate.Task;
            }
        });
        bot.MapCallback("act", ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Callbacks.Enqueue(ctx);
            return Task.CompletedTask;
        });
        bot.MapCallback("answer", async ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Callbacks.Enqueue(ctx);
            await ctx.AnswerAsync("Готово", true);
        });
        bot.MapCallback("twice", async ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Callbacks.Enqueue(ctx);
            await ctx.AnswerAsync("first", false);
            await ctx.AnswerAsync("second", true);
        });
        bot.MapCallback("boom", ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Callbacks.Enqueue(ctx);
            throw new InvalidOperationException("secret-boom-detail");
        });
        // 003: reply handlers. "name"/"other" record; "again" re-expects "name" (p2); "boom" throws.
        bot.MapReply("name", ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Replies.Enqueue(ctx);
            return Task.CompletedTask;
        });
        bot.MapReply("other", ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Replies.Enqueue(ctx);
            return Task.CompletedTask;
        });
        bot.MapReply("again", async ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Replies.Enqueue(ctx);
            await ctx.Services.GetRequiredService<IBotConversation>()
                .ExpectReplyAsync(ctx.From.Id, "demo", "name", "p2", ctx.CancellationToken);
        });
        bot.MapReply("boom", ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Replies.Enqueue(ctx);
            throw new InvalidOperationException("secret-boom-detail");
        });
        // 003: "demo:edit" edits the message the button is under.
        bot.MapCallback("edit", async ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Callbacks.Enqueue(ctx);
            await ctx.EditAsync(new OutgoingMessage("edited"));
        });
        bot.MapStart(ctx =>
        {
            ctx.Services.GetRequiredService<BotRecorder>().Starts.Enqueue(ctx);
            if (ctx.Payload == "boom") throw new InvalidOperationException("secret-boom-detail");
            return Task.CompletedTask;
        });
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapGet("/ping", (HttpContext context, PingCounter counter) =>
        {
            Interlocked.Increment(ref counter.Count);
            return Results.Json(context.GetTelegramUserId());
        });

        api.MapGet("/whoami", (HttpContext context) =>
        {
            var d = context.GetInitData();
            return Results.Json(new
            {
                id = d.User.Id,
                firstName = d.User.FirstName,
                lastName = d.User.LastName,
                username = d.User.Username,
                languageCode = d.User.LanguageCode,
                authDate = d.AuthDate.ToUnixTimeSeconds(),
                startParam = d.StartParam,
                queryId = d.QueryId,
                chatType = d.ChatType,
                chatInstance = d.ChatInstance,
            });
        });

        api.MapGet("/marker", (DemoMarker marker) => Results.Json(marker.Value));

        api.MapGet("/boom", IResult () => throw new InvalidOperationException("secret-boom-detail"));

        api.MapGet("/validation", () => ApiErrors.Validation(
            new Dictionary<string, string[]> { ["title"] = ["too long", "bad chars"] }));
        api.MapGet("/forbidden", () => ApiErrors.Forbidden());
        api.MapGet("/custom-not-found", () => ApiErrors.NotFound("demo_thing_not_found", "Thing not found"));
    }
}
