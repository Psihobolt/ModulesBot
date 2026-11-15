using System.Reflection;
using System.Text;
using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Modules.MovieNight.Tests.Helpers;
using static Modules.MovieNight.Tests.Helpers.BotDriver;

namespace Modules.MovieNight.Tests;

// TEST-6 (005): AC-17, AC-18
public class BotConstraintsAndParityTests
{
    private static readonly string Guid36 = Guid.NewGuid().ToString("D");

    private static RecordingRouteBuilder Routes()
    {
        var builder = new RecordingRouteBuilder();
        ((IBotModule)new MovieNightModule()).MapBot(builder);
        return builder;
    }

    // ---------------- AC-17 ----------------

    [Fact]
    public void AC17_KickOkWithNineDigitJoinSeq_Is64BytesAndDoesNotThrow()
    {
        var data = CallbackData.Create("movienight", "kickok", $"{Guid36}:999999999");

        Assert.Equal(64, Encoding.UTF8.GetByteCount(data));
    }

    [Fact]
    public void AC17_KickOkWithTenDigitJoinSeq_Is65BytesAndThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CallbackData.Create("movienight", "kickok", $"{Guid36}:1000000000"));
    }

    [Fact]
    public void AC17_OtherLongestActions_FitInto64Bytes()
    {
        Assert.Equal(57, CallbackData.Create("movienight", "newlinkok", Guid36).Length);
        Assert.Equal(55, CallbackData.Create("movienight", "members", Guid36).Length);
        Assert.Equal(26, CallbackData.Create("movienight", "join", "AB12cd34EF").Length);
    }

    [Fact]
    public async Task AC17_AllButtonsOnAllScreensWithNineDigitJoinSeq_FitInto64Bytes()
    {
        await using var f = await NewAsync();
        var (id, code) = await f.GroupWithAsync("Мы с Петей", 1);
        // Следующие членства получат JoinSeq из 9 цифр (AUTOINCREMENT продолжает с sqlite_sequence).
        f.Scalar("UPDATE sqlite_sequence SET seq = 999999990 WHERE name = 'movienight_group_members'");
        await f.JoinAsync(2, code);
        await f.JoinAsync(3, code);
        f.Sender.Clear();
        var seq = f.Scalar("SELECT MAX(JoinSeq) FROM movienight_group_members");
        Assert.InRange(seq, 999999991, 999999999);

        var screens = new List<OutgoingMessage>();
        foreach (var data in new[]
                 {
                     "groups", $"g:{id}", $"members:{id}", $"kick:{id}:{seq}", $"leave:{id}", $"newlink:{id}",
                 })
        {
            screens.Add((await f.Click(1, "movienight:" + data)).Edit.Message);
        }

        screens.Add((await f.Click(3, $"movienight:g:{id}")).Edit.Message);
        screens.Add((await f.Start(4, "movienight_join_" + code)).SendTo(4).Message);
        var kicked = await f.Click(1, $"movienight:kickok:{id}:{seq}");
        screens.Add(kicked.Edit.Message);

        var callbacks = screens.SelectMany(Buttons)
            .Select(b => b[(b.LastIndexOf('|') + 1)..])
            .Where(d => !d.StartsWith("web:", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(callbacks);
        Assert.All(callbacks, d => Assert.InRange(Encoding.UTF8.GetByteCount(d), 1, 64));
        Assert.Contains(callbacks, d => d.StartsWith("movienight:kick:", StringComparison.Ordinal) && d.EndsWith(":" + seq, StringComparison.Ordinal));
        Assert.Equal("Вася исключён(а) из группы", kicked.AnswerText);
    }

    // ---------------- AC-18 ----------------

    [Fact]
    public void AC18_ModuleRoutes_AreExactlyTheSpecifiedSet()
    {
        var routes = Routes();

        Assert.Equal(["groups", "newgroup"], routes.Commands.Select(c => c.Command));
        Assert.Equal(
            new[] { "groups", "new", "cancel", "nojoin", "g", "invite", "leave", "leaveok", "rename", "newlink", "newlinkok", "members", "kick", "kickok", "join" }
                .Order(StringComparer.Ordinal),
            routes.Callbacks.Order(StringComparer.Ordinal));
        Assert.Equal(["newname", "rename"], routes.Replies.Order(StringComparer.Ordinal));
        Assert.Equal(1, routes.Starts);
        Assert.Equal(routes.Callbacks.Count, routes.Callbacks.Distinct().Count());
    }

    [Fact]
    public void AC18_CommandDescriptions_AreFromSpec()
    {
        var routes = Routes();

        Assert.Equal([("groups", "мои группы"), ("newgroup", "создать группу")], routes.Commands);
    }

    // Строка паритета -> действия бота (маршруты) -> префиксы тестов бота (005) и Mini App (004, фронтенд).
    private sealed record ParityRow(string Function, string[] BotRoutes, string[] BotTestPrefixes, string[] MiniAppTestPrefixes);

    private static readonly ParityRow[] Parity =
    [
        new("Список групп", ["cmd:groups", "cb:groups"], ["AC2_", "AC7_"], ["AC23:"]),
        new("Создать группу", ["cmd:newgroup", "cb:new", "reply:newname", "cb:cancel"], ["AC3_", "AC4_", "AC5_"], ["AC24:"]),
        new("Участники, порядок, владелец", ["cb:g"], ["AC6_"], ["AC25:"]),
        new("Пригласить", ["cb:invite"], ["AC10_"], ["AC25:"]),
        new("Вступить по ссылке", ["start", "cb:join", "cb:nojoin"], ["AC15_", "AC16_"], ["AC27:", "AC28:"]),
        new("Выйти (с передачей владения)", ["cb:leave", "cb:leaveok"], ["AC14_"], ["AC25:", "AC26:"]),
        new("Переименовать", ["cb:rename", "reply:rename"], ["AC11_"], ["AC29:"]),
        new("Перевыпустить ссылку", ["cb:newlink", "cb:newlinkok"], ["AC12_"], ["AC26:"]),
        new("Исключить участника", ["cb:members", "cb:kick", "cb:kickok"], ["AC13_", "AC13a_"], ["AC26:"]),
    ];

    [Fact]
    public void AC18_EveryParityRowHasBotRoutesAndBotTests_AndEveryRouteBelongsToARow()
    {
        var routes = Routes();
        var registered = routes.Commands.Select(c => "cmd:" + c.Command)
            .Concat(routes.Callbacks.Select(c => "cb:" + c))
            .Concat(routes.Replies.Select(r => "reply:" + r))
            .Append("start")
            .ToHashSet();
        var mapped = Parity.SelectMany(p => p.BotRoutes).ToHashSet();

        Assert.Equal(registered.Order(), mapped.Order());

        var botTests = new[] { typeof(BotCommandsTests), typeof(BotNavigationTests), typeof(BotOwnerActionsTests), typeof(BotJoinTests) }
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().Any(a => a is FactAttribute))
            .Select(m => m.Name)
            .ToArray();
        foreach (var row in Parity)
        {
            foreach (var prefix in row.BotTestPrefixes)
            {
                Assert.True(botTests.Any(n => n.StartsWith(prefix, StringComparison.Ordinal)), $"{row.Function}: нет теста бота {prefix}*");
            }
        }
    }

    [Fact]
    public void AC18_EveryParityRowHasMiniAppTest()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var dir = Path.Combine(root!, "webapp", "src", "modules", "movie-night");
        var text = string.Concat(Directory.EnumerateFiles(dir, "*.test.ts*", SearchOption.AllDirectories).Select(File.ReadAllText));

        foreach (var row in Parity)
        {
            foreach (var prefix in row.MiniAppTestPrefixes)
            {
                Assert.True(text.Contains($"it('{prefix}", StringComparison.Ordinal), $"{row.Function}: нет теста Mini App {prefix}*");
            }
        }
    }
}
