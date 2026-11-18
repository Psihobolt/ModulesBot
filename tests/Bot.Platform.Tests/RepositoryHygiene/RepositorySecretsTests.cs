namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>AC-3 (+AC-5): в репозитории нет секретов. Сообщение содержит только путь:строка и id правила.</summary>
public class RepositorySecretsTests
{
    // Запись: (путь, id правила, причина). Только доказанно несекретные совпадения.
    private static readonly IReadOnlyList<AllowEntry> Allowlist = [];

    [Fact]
    public void AC3_RepositoryFiles_ContainNoSecrets()
    {
        var candidates = GitRepo.ListZ("ls-files", "--cached", "--others", "--exclude-standard");

        // Контроль: набор кандидатов реален (иначе тест зелёный по неверной причине).
        Assert.Contains("README.md", candidates);
        Assert.Contains("deploy/server/multibot.env.example", candidates);

        var scanned = 0;
        var findings = new List<SecretFinding>();
        foreach (var rel in candidates)
        {
            if (!File.Exists(Path.Combine(GitRepo.Root, rel))) continue; // удалён с диска / каталог
            scanned++;
            findings.AddRange(SecretScanner.ScanFile(GitRepo.Root, rel));
        }

        Assert.True(scanned > 50, $"Просканировано подозрительно мало файлов: {scanned}.");

        var remaining = SecretScanner.ApplyAllowlist(findings, Allowlist).ToList();
        Assert.True(remaining.Count == 0,
            "Найдены возможные секреты (значения скрыты): " + string.Join("; ", remaining));
    }

    [Fact]
    public void AC3_LocalSettingsWithRealKey_IsNotCandidate()
    {
        var candidates = GitRepo.ListZ("ls-files", "--cached", "--others", "--exclude-standard");
        Assert.DoesNotContain(".claude/settings.local.json", candidates);
    }
}
