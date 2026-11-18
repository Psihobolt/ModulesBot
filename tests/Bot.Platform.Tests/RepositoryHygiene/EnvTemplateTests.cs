using System.Text.RegularExpressions;

namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>006 AC-5/AC-6 и 008 AC-3..AC-5: deploy/server/multibot.env.example против README («Секреты и конфигурация») и блока из чек-листа.</summary>
public class EnvTemplateTests
{
    private const string TemplatePath = "deploy/server/multibot.env.example";

    // Ключи и значения по умолчанию из исходного блока DEPLOY_CHECKLIST §2.
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["ASPNETCORE_URLS"] = "http://127.0.0.1:5080",
        ["Bot__PublicUrl"] = "https://bot-test.example.com",
        ["WebApp__DistPath"] = "/opt/multibot/webapp",
        ["ConnectionStrings__Default"] = "Data Source=/var/lib/multibot/bot.db",
    };

    private static readonly string[] SecretKeys = ["Bot__Token", "Bot__WebhookSecret"];

    private static readonly Regex KeyRegex = new("^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>
    /// Нарушения формата env-шаблона (008 AC-3). Сообщения: файл, номер строки, ключ; значение и содержимое
    /// строк без '=' не выводятся.
    /// </summary>
    internal static List<string> Violations(string label, string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                result.Add($"{label}:{lineNo}: строка не вида KEY=VALUE (содержимое скрыто)");
                continue;
            }

            var key = line[..eq];
            if (!KeyRegex.IsMatch(key)) result.Add($"{label}:{lineNo}: недопустимое имя ключа '{key}'");
            else if (!seen.Add(key)) result.Add($"{label}:{lineNo}: повтор ключа '{key}'");
        }

        return result;
    }

    /// <summary>Общий разбор шаблона: им пользуются и чтение файла, и тесты на сообщения об ошибках.</summary>
    internal static Dictionary<string, string> ReadTemplate(string text, string label = TemplatePath)
    {
        var violations = Violations(label, text);
        Assert.True(violations.Count == 0, string.Join("; ", violations));
        var result = new Dictionary<string, string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            result[line[..eq]] = line[(eq + 1)..];
        }

        return result;
    }

    internal static Dictionary<string, string> ReadTemplateFile(string relativePath)
    {
        var full = Path.Combine(GitRepo.Root, relativePath);
        Assert.True(File.Exists(full), $"Нет файла {relativePath}.");
        return ReadTemplate(File.ReadAllText(full), relativePath);
    }

    private static Dictionary<string, string> ReadTemplate() => ReadTemplateFile(TemplatePath);

    private static HashSet<string> ReadReadmeServerKeys()
    {
        var lines = File.ReadAllLines(Path.Combine(GitRepo.Root, "README.md"));
        var start = Array.FindIndex(lines, l => l.StartsWith('#') && l.Contains("Секреты и конфигурация", StringComparison.Ordinal));
        Assert.True(start >= 0, "В README нет раздела «Секреты и конфигурация».");

        var keys = new HashSet<string>();
        var serverCol = -1;
        var rows = 0;
        for (var i = start + 1; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (l.StartsWith('#')) break;
            if (!l.StartsWith('|')) continue;
            var cells = l.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (serverCol < 0)
            {
                serverCol = Array.FindIndex(cells, c => c.Contains("сервер", StringComparison.OrdinalIgnoreCase));
                Assert.True(serverCol > 0, "В заголовке таблицы README нет колонки «Сервер».");
                continue;
            }

            if (cells.All(c => Regex.IsMatch(c, "^:?-+:?$"))) continue; // разделитель
            rows++;
            var m = Regex.Match(cells[0], "`([^`]+)`");
            Assert.True(m.Success, $"Ключ в первой колонке должен быть в обратных кавычках: строка {rows}.");
            var key = m.Groups[1].Value.Replace(":", "__");
            var mark = cells[serverCol].ToLowerInvariant();
            if (mark.StartsWith("да", StringComparison.Ordinal)) keys.Add(key);
            else Assert.True(mark is "нет" or "—" or "-" or "", $"Неизвестная пометка «Сервер» для {key}.");
        }

        Assert.True(rows >= 10, $"Таблица README слишком короткая: {rows} строк.");
        return keys;
    }

    [Fact]
    public void AC6_TemplateKeys_EqualChecklistKeys()
    {
        var expected = new HashSet<string>(Defaults.Keys.Concat(SecretKeys));
        Assert.Equal(expected.Order(), ReadTemplate().Keys.Order());
    }

    [Fact]
    public void AC6_TemplateKeys_EqualReadmeServerKeys()
    {
        var readme = ReadReadmeServerKeys();
        Assert.NotEmpty(readme);
        Assert.Equal(readme.Order(), ReadTemplate().Keys.Order());
    }

    [Fact]
    public void AC6_SecretValues_ArePlaceholders()
    {
        var t = ReadTemplate();
        foreach (var k in SecretKeys)
        {
            Assert.True(t.TryGetValue(k, out var v), $"Нет ключа {k}.");
            AssertPlaceholder(k, v);
        }
    }

    // Сообщение при падении не содержит значения (AC-5): только имя ключа.
    private static void AssertPlaceholder(string key, string? value) =>
        Assert.True(value is not null && Regex.IsMatch(value, "^<.+>$"), $"{key}: значение не заглушка <…>.");

    [Fact]
    public void AC5_PlaceholderCheck_FailureMessage_DoesNotLeakValue()
    {
        const string leaked = "leakedvalue777";
        var ex = Assert.ThrowsAny<Exception>(() => AssertPlaceholder("Bot__Token", leaked));
        Assert.Contains("Bot__Token", ex.Message);
        Assert.DoesNotContain(leaked, ex.Message);
        Assert.DoesNotContain("leaked", ex.Message);
        AssertPlaceholder("Bot__Token", "<token>"); // заглушка проходит
    }

    [Fact]
    public void AC5_MalformedTemplateLine_FailureMessage_DoesNotLeakContent()
    {
        // Тот же ReadTemplate(text), что читает файл; образец встроен, репозиторий не трогаем.
        const string sample = "ASPNETCORE_ENVIRONMENT=Production\nleakedvalue777\n";
        var ex = Assert.ThrowsAny<Exception>(() => ReadTemplate(sample, "sample.env"));
        Assert.Contains("sample.env:2", ex.Message);
        Assert.DoesNotContain("leaked", ex.Message);
    }

    [Fact]
    public void AC6_NonSecretValues_AreChecklistDefaults()
    {
        var t = ReadTemplate();
        foreach (var (k, v) in Defaults)
        {
            Assert.Equal(v, t[k]);
        }
    }

    [Fact]
    public void AC6_Template_MentionsTargetPathAndPermissions()
    {
        var text = File.ReadAllText(Path.Combine(GitRepo.Root, TemplatePath));
        Assert.Contains("/etc/multibot/multibot.env", text);
        Assert.Contains("600", text);
    }

    [Fact]
    public void AC6_Template_PassesSecretScan() =>
        Assert.Empty(SecretScanner.ScanFile(GitRepo.Root, TemplatePath));

    [Fact]
    public void AC6_Template_ScannerWouldCatchRealToken()
    {
        // Контроль «тест умеет краснеть»: тот же путь с реальным значением помечается R5.
        var bad = SecretScanner.Scan(TemplatePath, "Bot__Token=" + "1234567" + "89:" + new string('A', 35));
        Assert.Contains(bad, f => f.RuleId == "R5");
    }

    // ---------- 008 AC-3: имена переменных для systemd и bash ----------

    private static IReadOnlyList<string> AllEnvExamples()
    {
        var dir = Path.Combine(GitRepo.Root, "deploy");
        Assert.True(Directory.Exists(dir), "Нет каталога deploy.");
        return Directory.EnumerateFiles(dir, "*.env.example", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(GitRepo.Root, f).Replace('\\', '/')).Order().ToList();
    }

    [Fact]
    public void S008_AC3_AllDeployEnvExamples_HaveValidKeysAndNoDuplicates()
    {
        var files = AllEnvExamples();
        // Контроль: оба шаблона найдены, иначе проверка ничего не доказывает.
        Assert.Contains("deploy/server/updater.env.example", files);
        Assert.Contains("deploy/server/multibot.env.example", files);

        var violations = files.SelectMany(f => Violations(f, File.ReadAllText(Path.Combine(GitRepo.Root, f)))).ToList();
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Fact]
    public void S008_AC3_KeyWithDot_IsViolation_NamingFileLineAndKey_WithoutValue()
    {
        const string sample = "# комментарий\nASPNETCORE_ENVIRONMENT=Production\nLogging__LogLevel__Microsoft.EntityFrameworkCore=hiddenvalue42\n";
        var violations = Violations("sample.env.example", sample);
        var single = Assert.Single(violations);
        Assert.Contains("sample.env.example", single);
        Assert.Contains(":3", single);
        Assert.Contains("Logging__LogLevel__Microsoft.EntityFrameworkCore", single);
        Assert.DoesNotContain("hiddenvalue42", single);
    }

    [Fact]
    public void S008_AC3_DuplicateKey_IsViolation()
    {
        var violations = Violations("s.env", "A=1\nB=2\nA=3\n");
        Assert.Contains(violations, v => v.Contains("s.env:3") && v.Contains("'A'"));
    }

    // ---------- 008 AC-5: шаблон автообновления ----------

    private const string UpdaterPath = "deploy/server/updater.env.example";

    [Fact]
    public void S008_AC5_UpdaterTemplateKeys_EqualScriptResetVariables()
    {
        var script = File.ReadAllText(Path.Combine(GitRepo.Root, "deploy/server/multibot-update.sh"));
        var reset = DeployServerTests.ResetVariables(script);
        Assert.Equal(["GITHUB_TOKEN", "NOTIFY_CHAT_ID", "REPO", "WEBHOOK_IP"], reset.Order());
        Assert.Equal(reset.Order(), ReadTemplateFile(UpdaterPath).Keys.Order());
    }

    [Fact]
    public void S008_AC5_UpdaterTemplate_GithubTokenIsEmpty()
    {
        var t = ReadTemplateFile(UpdaterPath);
        Assert.True(t.TryGetValue("GITHUB_TOKEN", out var v), "Нет ключа GITHUB_TOKEN.");
        Assert.True(v.Length == 0, "GITHUB_TOKEN в шаблоне должен быть пуст.");
    }

    [Fact]
    public void S008_AC5_UpdaterTemplate_MentionsTargetPathAndPermissions()
    {
        var text = File.ReadAllText(Path.Combine(GitRepo.Root, UpdaterPath));
        Assert.Contains("/etc/multibot/updater.env", text);
        Assert.Contains("600", text);
    }

    [Fact]
    public void S008_AC5_UpdaterTemplate_PassesSecretScan() =>
        Assert.Empty(SecretScanner.ScanFile(GitRepo.Root, UpdaterPath));

    // ---------- 008 AC-4: шаблон бота ----------

    [Fact]
    public void S008_AC4_BotTemplate_HasNoEfCoreKey()
    {
        var keys = ReadTemplate().Keys;
        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, k => k.Contains("EntityFrameworkCore", StringComparison.Ordinal));
    }
}
