using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>008 AC-8 (appsettings.json, EF Core) и AC-9 (ссылки deploy/… в документах).</summary>
public class RepositoryConfigDocsTests
{
    private const string EfEnvKey = "Logging__LogLevel__Microsoft.EntityFrameworkCore";

    private static string Read(string rel)
    {
        var full = Path.Combine(GitRepo.Root, rel);
        Assert.True(File.Exists(full), $"Нет файла {rel}.");
        return File.ReadAllText(full);
    }

    // ---------- AC-8 ----------

    private static JsonElement LogLevels()
    {
        using var doc = JsonDocument.Parse(Read("src/Bot.Host/appsettings.json"));
        return doc.RootElement.GetProperty("Logging").GetProperty("LogLevel").Clone();
    }

    [Fact]
    public void AC8_Appsettings_SetsEfCoreToWarning()
    {
        var levels = LogLevels();
        Assert.True(levels.TryGetProperty("Microsoft.EntityFrameworkCore", out var ef), "Нет Microsoft.EntityFrameworkCore.");
        Assert.Equal("Warning", ef.GetString());
    }

    [Fact]
    public void AC8_Appsettings_KeepsDefaultAndAspNetCore()
    {
        var levels = LogLevels();
        Assert.Equal("Information", levels.GetProperty("Default").GetString());
        Assert.Equal("Warning", levels.GetProperty("Microsoft.AspNetCore").GetString());
    }

    [Fact]
    public void AC8_EfCoreEnvKey_IsNotInTemplatesOrReadme()
    {
        var files = GitRepo.ListZ("ls-files", "--cached", "--others", "--exclude-standard")
            .Where(f => f.EndsWith(".env.example", StringComparison.Ordinal) && File.Exists(Path.Combine(GitRepo.Root, f)))
            .Append("README.md").ToList();
        Assert.Contains("deploy/server/multibot.env.example", files); // контроль: шаблон бота реально проверяется
        var offenders = files.Where(f => Read(f).Contains(EfEnvKey, StringComparison.Ordinal)).ToList();
        Assert.True(offenders.Count == 0, $"Ключ {EfEnvKey} найден в: " + string.Join(", ", offenders));
    }

    // ---------- AC-9 ----------

    private static readonly Regex DeployRef = new("deploy/[A-Za-z0-9._/-]*", RegexOptions.CultureInvariant);

    /// <summary>Пути deploy/…, на которые нет файла или каталога; завершающая точка (конец предложения) отбрасывается.</summary>
    internal static List<string> BrokenDeployRefs(string text, Func<string, bool> exists) =>
        DeployRef.Matches(text).Select(m => m.Value.TrimEnd('.')).Where(p => !exists(p)).Distinct().ToList();

    private static bool Exists(string rel)
    {
        var full = Path.Combine(GitRepo.Root, rel);
        return File.Exists(full) || Directory.Exists(full);
    }

    private static IEnumerable<string> Documents()
    {
        var wf = Directory.GetFiles(Path.Combine(GitRepo.Root, ".github", "workflows"), "*.yml")
            .Select(f => Path.GetRelativePath(GitRepo.Root, f).Replace('\\', '/'));
        return new[] { "README.md", "DEPLOY_CHECKLIST.md" }.Concat(wf);
    }

    [Fact]
    public void AC9_DeployReferences_PointToExistingPaths()
    {
        var docs = Documents().ToList();
        Assert.Contains(".github/workflows/release.yml", docs);
        var total = docs.Sum(d => DeployRef.Matches(Read(d)).Count);
        Assert.True(total >= 3, $"Подозрительно мало ссылок deploy/…: {total}."); // контроль: тест что-то проверяет

        var broken = docs.SelectMany(d => BrokenDeployRefs(Read(d), Exists).Select(p => $"{d}: {p}")).ToList();
        Assert.True(broken.Count == 0, "Ссылки на несуществующие пути: " + string.Join("; ", broken));
    }

    [Fact]
    public void AC9_OldTemplatePath_IsNotMentioned()
    {
        var offenders = Documents().Where(d => Read(d).Contains("deploy/multibot.env.example", StringComparison.Ordinal)).ToList();
        Assert.True(offenders.Count == 0, "Упоминается deploy/multibot.env.example: " + string.Join(", ", offenders));
    }

    [Fact]
    public void AC9_Checker_DetectsMissingPath_AndIgnoresSentenceDot()
    {
        const string text = "см. deploy/server/multibot-update.sh. Также deploy/nope/missing.txt, и каталог deploy/server/.";
        var broken = BrokenDeployRefs(text, Exists);
        Assert.Equal(["deploy/nope/missing.txt"], broken);
    }
}
