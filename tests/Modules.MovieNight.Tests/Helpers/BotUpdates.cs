using Bot.Platform.Tests.Helpers;
using Telegram.Bot.Types;

namespace Modules.MovieNight.Tests.Helpers;

/// <summary>
/// Обновления от пользователя N с тем же именем, что при посеве (SeedDefaultsAsync): диспетчер на каждом
/// обновлении обновляет запись реестра именем из from, поэтому firstName/lastName/username обязаны совпадать.
/// </summary>
public static class BotUpdates
{
    private static readonly string[] Names = ["Аня", "Петя", "Вася", "Гоша", "Дима", "Егор", "Женя"];
    private static int _query;

    public static string NameOf(long userId) => Names[(int)((userId - 1) % Names.Length)];

    public static Update Text(long userId, string text) =>
        TestUpdates.Private(text, userId, NameOf(userId), lastName: null, username: null);

    public static Update Command(long userId, string command, string? args = null) =>
        Text(userId, "/" + command + (args is null ? "" : " " + args));

    public static Update Start(long userId, string? payload = null) =>
        Text(userId, payload is null ? "/start" : "/start " + payload);

    public static Update Click(long userId, string data, int messageId = 4242) =>
        TestUpdates.Callback(data, userId, "q-" + Interlocked.Increment(ref _query), messageId: messageId,
            firstName: NameOf(userId), lastName: null, username: null);
}
