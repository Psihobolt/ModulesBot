namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>AC-1/AC-2: .gitignore. Глобальные исключения пользователя нейтрализованы в GitRepo.</summary>
public class GitIgnoreTests
{
    public static TheoryData<string> IgnoredPaths =>
    [
        ".claude/settings.local.json", ".env", ".env.local", ".env.production", "deploy/multibot.env", "foo.env",
        "src/Bot.Host/appsettings.Local.json", "src/Bot.Host/appsettings.Development.local.json", "secrets.json",
        "certs/server.pfx", "certs/server.p12", "certs/server.pem", "certs/server.key", "keys/strong.snk",
        "msbuild.binlog", "logs/app.log", "npm-debug.log", "coverage/index.html", ".vscode/settings.json",
        "src/Bot.Host/data/bot.db", "bot.db-journal", "webapp/.env.local", "webapp/.env.production.local",
        "webapp/coverage/index.html", "webapp/vite.log", "out/app/Bot.Host.dll", ".DS_Store",
    ];

    public static TheoryData<string> NotIgnoredPaths =>
    [
        ".gitignore", ".mcp.json", "src/Bot.Host/appsettings.json", "src/Bot.Host/Properties/launchSettings.json",
        "deploy/multibot.env.example", ".env.example", "webapp/.env.example", ".vscode/extensions.json",
        ".claude/settings.json", ".claude/agents/architect.md", "webapp/public/telegram-web-app.js",
        "webapp/package-lock.json",
    ];

    [Theory]
    [MemberData(nameof(IgnoredPaths))]
    public void AC1_PathWithSecretOrLocalData_IsIgnored(string path) =>
        Assert.True(GitRepo.IsIgnored(path), $"Путь '{path}' должен игнорироваться .gitignore (без глобальных правил).");

    [Theory]
    [MemberData(nameof(NotIgnoredPaths))]
    public void AC2_SharedPath_IsNotIgnored(string path) =>
        Assert.False(GitRepo.IsIgnored(path), $"Путь '{path}' не должен игнорироваться.");

    [Fact]
    public void AC2_TrackedFiles_DoNotMatchIgnoreRules()
    {
        // Контроль, что git вообще видит файлы: иначе пустой результат ничего не доказывает.
        Assert.NotEmpty(GitRepo.ListZ("ls-files", "--cached"));

        var tracked = GitRepo.ListZ("ls-files", "--cached", "--ignored", "--exclude-standard");
        Assert.True(tracked.Count == 0,
            "Отслеживаемые файлы под правилами игнорирования: " + string.Join(", ", tracked));
    }
}
