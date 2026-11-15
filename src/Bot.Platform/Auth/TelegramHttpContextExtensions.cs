using Microsoft.AspNetCore.Http;

namespace Bot.Platform.Auth;

public static class TelegramHttpContextExtensions
{
    internal static readonly object InitDataKey = new();

    public static InitData GetInitData(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(InitDataKey, out var value) && value is InitData data
            ? data
            : throw new InvalidOperationException("The request is not authorized with Telegram initData.");
    }

    public static long GetTelegramUserId(this HttpContext context) => context.GetInitData().User.Id;
}
