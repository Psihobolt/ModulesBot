using System.Text.RegularExpressions;

namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>AC-6: deploy/multibot.env.example против README («Секреты и конфигурация») и блока из чек-листа.</summary>
public class EnvTemplateTests
{
    private const string TemplatePath = "deploy/multibot.env.example";

    // Ключи и значения по умолчанию из исходного блока DEPLOY_CHECKLIST §2.
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["ASPNETCORE_URLS"] = "http://127.0.0.1:5080",
        ["Bot__PublicUrl"] = "https://bot-test.example.com",
        ["WebApp__DistPath"] = "/opt/multibot/webapp",
        ["ConnectionStrings__Default"] = "Data Source=/var/lib/multibot/bot.db",
        ["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "Warning",
    };

    private static readonly string[] SecretKeys = ["Bot__Token", "Bot__WebhookSecret"];

    private static Dictionary<string, string> ReadTemplate()
    {
        var full = Path.Combine(GitRepo.Root, TemplatePath);
        Assert.True(File.Exists(full), $"Нет файла {TemplatePath}.");
        var result = new Dictionary<string, string>();
        var lineNo = 0;
        foreach (var raw in File.ReadAllLines(full))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            Assert.True(eq > 0, $"Строка {lineNo} шаблона не вида KEY=VALUE (содержимое скрыто).");
            Assert.True(result.TryAdd(line[..eq], line[(eq + 1)..]), $"Дубликат ключа {line[..eq]}.");
        }

        return result;
    }

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
        var path = Path.Combine(GitRepo.Root, TemplatePath);
        var original = File.ReadAllText(path);
        // Читаем шаблон без записи в репозиторий: проверяем тот же формат сообщения на строке без '='.
        const string leaked = "leakedvalue777";
        var ex = Assert.ThrowsAny<Exception>(() => ParseForTest(original + "\n" + leaked + "\n"));
        Assert.DoesNotContain("leaked", ex.Message);
    }

    private static void ParseForTest(string text)
    {
        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            Assert.True(line.IndexOf('=') > 0, $"Строка {lineNo} шаблона не вида KEY=VALUE (содержимое скрыто).");
        }
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
}
