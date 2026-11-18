using System.Text.RegularExpressions;

namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>
/// 008 AC-1, AC-6, AC-7, AC-10: файлы автообновления сервера в deploy/server и их согласованность с проектом.
/// Скрипт не запускается, сеть не используется — только разбор текста.
/// </summary>
public class DeployServerTests
{
    private const string Dir = "deploy/server/";
    private const string Script = Dir + "multibot-update.sh";
    private const string Service = Dir + "multibot-update.service";
    private const string Timer = Dir + "multibot-update.timer";
    private const string UpdaterEnv = Dir + "updater.env.example";
    private const string BotEnv = Dir + "multibot.env.example";

    private static string Read(string rel)
    {
        var full = Path.Combine(GitRepo.Root, rel);
        Assert.True(File.Exists(full), $"Нет файла {rel}.");
        var text = File.ReadAllText(full);
        Assert.NotEmpty(text); // пустой файл ничего не доказывает
        return text;
    }

    // ---------- AC-1 ----------

    [Theory]
    [InlineData(Script)]
    [InlineData(Service)]
    [InlineData(Timer)]
    [InlineData(UpdaterEnv)]
    [InlineData(BotEnv)]
    public void AC1_ServerFile_Exists(string rel) =>
        Assert.True(File.Exists(Path.Combine(GitRepo.Root, rel)), $"Нет файла {rel}.");

    [Fact]
    public void AC1_OldTemplateLocation_IsGone()
    {
        Assert.False(File.Exists(Path.Combine(GitRepo.Root, "deploy/multibot.env.example")),
            "Старый шаблон deploy/multibot.env.example должен быть перенесён.");
        // Индекс git: запись об удалении на диске допустима до коммита, поэтому учитываем только существующие файлы.
        var tracked = GitRepo.ListZ("ls-files", "--cached", "--", "deploy/multibot.env.example");
        Assert.DoesNotContain(tracked, t => File.Exists(Path.Combine(GitRepo.Root, t)));
    }

    [Fact]
    public void AC1_ScriptMode_InGitIndex_Is100755()
    {
        var r = GitRepo.Run("ls-files", "-s", "--", Script);
        Assert.Equal(0, r.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(r.StdOut), $"{Script} не в индексе git.");
        Assert.Equal("100755", r.StdOut.Split(' ')[0]);
    }

    [Fact]
    public void AC1_Script_HasBashShebangAndStrictMode()
    {
        var text = Read(Script);
        Assert.Equal("#!/usr/bin/env bash", text.Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("set -euo pipefail", text);
    }

    // ---------- AC-6: значения по умолчанию скрипта ----------

    private static readonly Regex DefaultLine = new(
        @"^(?<n>[A-Z][A-Z0-9_]*)=(?:\$\{MBU_[A-Z0-9_]+:-(?<d>[^}]*)\}|(?<d>[^\s;]+))[ \t]*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static Dictionary<string, string> ScriptDefaults(string script)
    {
        var d = new Dictionary<string, string>();
        foreach (Match m in DefaultLine.Matches(script.Replace("\r", string.Empty)))
        {
            d.TryAdd(m.Groups["n"].Value, m.Groups["d"].Value);
        }

        return d;
    }

    /// <summary>Переменные из строки «REPO=; GITHUB_TOKEN=; …» (обнуление перед чтением конфигурации).</summary>
    internal static List<string> ResetVariables(string script)
    {
        var line = script.Split('\n').FirstOrDefault(l => l.StartsWith("REPO=;", StringComparison.Ordinal));
        Assert.True(line is not null, "В скрипте нет строки обнуления «REPO=; …».");
        var names = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.TrimEnd('=')).ToList();
        Assert.NotEmpty(names);
        return names;
    }

    /// <summary>Имена переменных скрипта (а–ж), чьё значение не согласовано с проектом или не найдено.</summary>
    internal static List<string> ConsistencyFailures(
        string script, string updaterEnv, string botEnv, string releaseYml, string checklist)
    {
        var failed = new List<string>();
        var d = ScriptDefaults(script);
        var bot = EnvTemplateTests.ReadTemplate(botEnv, BotEnv);

        void Check(string name, Func<string, bool> ok)
        {
            if (!d.TryGetValue(name, out var v) || !ok(v)) failed.Add(name);
        }

        var firstLine = updaterEnv.Split('\n')[0];
        var confMatch = Regex.Match(firstLine, @"^#\s*(/\S+)");
        Check("CONF", v => confMatch.Success && v == confMatch.Groups[1].Value);
        Check("BOT_ENV", v => v == "/etc/multibot/multibot.env");

        var db = Regex.Match(bot.GetValueOrDefault("ConnectionStrings__Default") ?? string.Empty, @"Data Source=(.+)$");
        Check("DB", v => db.Success && v == db.Groups[1].Value);

        Check("BASE", v => bot.TryGetValue("WebApp__DistPath", out var dist) && v + "/webapp" == dist);

        var port = Regex.Match(bot.GetValueOrDefault("ASPNETCORE_URLS") ?? string.Empty, @":(\d+)/?$");
        Check("HEALTH_URL", v => port.Success && v == $"http://127.0.0.1:{port.Groups[1].Value}/health");

        Check("ASSET", v => v == "multibot.tar.gz"
            && Regex.IsMatch(releaseYml, @"-czf\s+multibot\.tar\.gz(\s|$)")
            && Regex.IsMatch(releaseYml, @"sha256sum\s+multibot\.tar\.gz\s*>\s*multibot\.tar\.gz\.sha256"));

        Check("SERVICE", v => v == "multibot" && checklist.Contains("/etc/systemd/system/multibot.service", StringComparison.Ordinal));
        return failed;
    }

    private static List<string> RealFailures() => ConsistencyFailures(
        Read(Script), Read(UpdaterEnv), Read(BotEnv), Read(".github/workflows/release.yml"), Read("DEPLOY_CHECKLIST.md"));

    [Fact]
    public void AC6_ScriptDefaults_AreConsistentWithProject()
    {
        var failures = RealFailures();
        Assert.True(failures.Count == 0, "Не согласованы переменные скрипта: " + string.Join(", ", failures));
    }

    [Fact]
    public void AC6_ScriptDefaults_ParserFindsAllVariables()
    {
        // Контроль парсера: без него «не найдено» и «согласовано» неразличимы.
        var d = ScriptDefaults(Read(Script));
        foreach (var n in new[] { "CONF", "BOT_ENV", "BASE", "DB", "SERVICE", "HEALTH_URL", "ASSET" })
        {
            Assert.True(d.ContainsKey(n), $"Парсер не нашёл {n}.");
        }

        Assert.Equal("/etc/multibot/updater.env", d["CONF"]); // форма ${MBU_X:-значение}
        Assert.Equal("multibot", d["SERVICE"]); // форма NAME=значение
    }

    private const string GoodSampleScript = """
        #!/usr/bin/env bash
        CONF=${MBU_CONF:-/etc/multibot/updater.env}
        BOT_ENV=${MBU_BOT_ENV:-/etc/multibot/multibot.env}
        BASE=${MBU_BASE:-/opt/multibot}
        DB=${MBU_DB:-/var/lib/multibot/bot.db}
        SERVICE=multibot
        HEALTH_URL=http://127.0.0.1:5080/health
        ASSET=multibot.tar.gz
        """;

    [Fact]
    public void AC6_SampleScript_WithOtherPort_FailsOnlyHealthUrl()
    {
        var updater = Read(UpdaterEnv);
        var bot = Read(BotEnv);
        var yml = Read(".github/workflows/release.yml");
        var checklist = Read("DEPLOY_CHECKLIST.md");

        // Эталонный образец проходит (иначе негатив не доказывает причину).
        Assert.Empty(ConsistencyFailures(GoodSampleScript, updater, bot, yml, checklist));

        var bad = GoodSampleScript.Replace(":5080/", ":5081/", StringComparison.Ordinal);
        Assert.NotEqual(GoodSampleScript, bad);
        Assert.Equal(["HEALTH_URL"], ConsistencyFailures(bad, updater, bot, yml, checklist));
    }

    [Fact]
    public void AC6_MissingVariable_IsReportedByName()
    {
        var script = GoodSampleScript.Replace("ASSET=multibot.tar.gz", string.Empty, StringComparison.Ordinal);
        var failed = ConsistencyFailures(script, Read(UpdaterEnv), Read(BotEnv),
            Read(".github/workflows/release.yml"), Read("DEPLOY_CHECKLIST.md"));
        Assert.Equal(["ASSET"], failed);
    }

    // ---------- AC-7: unit-файлы ----------

    private static Dictionary<string, List<string>> Sections(string ini)
    {
        var result = new Dictionary<string, List<string>>();
        List<string>? current = null;
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = result[line] = [];
            }
            else
            {
                Assert.True(current is not null, "Строка вне секции unit-файла.");
                current.Add(line);
            }
        }

        return result;
    }

    [Fact]
    public void AC7_Service_IsOneshotWithExpectedExecStart()
    {
        var s = Sections(Read(Service));
        Assert.True(s.ContainsKey("[Service]"), "Нет секции [Service].");
        Assert.Contains("Type=oneshot", s["[Service]"]);
        Assert.Contains("ExecStart=/usr/local/sbin/multibot-update", s["[Service]"]);
    }

    [Fact]
    public void AC7_Checklist_InstallsScriptToExecStartPath()
    {
        var checklist = Read("DEPLOY_CHECKLIST.md");
        Assert.Matches(@"install\b[^\n]*\s/usr/local/sbin/multibot-update(?![\w./-])", checklist);
    }

    [Fact]
    public void AC7_Timer_HasTimerAndInstallSections_AndTargetsUpdateService()
    {
        var s = Sections(Read(Timer));
        Assert.True(s.ContainsKey("[Timer]"), "Нет секции [Timer].");
        Assert.True(s.ContainsKey("[Install]"), "Нет секции [Install].");
        Assert.Contains("WantedBy=timers.target", s["[Install]"]);
        var unit = s["[Timer]"].Where(l => l.StartsWith("Unit=", StringComparison.Ordinal)).ToList();
        Assert.All(unit, l => Assert.Equal("Unit=multibot-update.service", l));
    }

    // ---------- AC-10: нет старой ручной выкладки ----------

    [Theory]
    [InlineData("rsync")]
    [InlineData("chown -R multibot")]
    public void AC10_Checklist_HasNoManualDeployRemnants(string forbidden) =>
        Assert.DoesNotContain(forbidden, Read("DEPLOY_CHECKLIST.md"), StringComparison.Ordinal);

    /// <summary>Строки с app.prev, где его копируют или переносят (cp/mv); удаление (rm) допустимо.</summary>
    internal static List<string> AppPrevViolations(string text)
    {
        var result = new List<string>();
        var lineNo = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNo++;
            if (!line.Contains("app.prev", StringComparison.Ordinal)) continue;
            if (line.Contains("cp ", StringComparison.Ordinal) || line.Contains("mv ", StringComparison.Ordinal))
            {
                result.Add($"DEPLOY_CHECKLIST.md:{lineNo}: app.prev копируется/переносится (cp/mv)");
            }
        }

        return result;
    }

    [Fact]
    public void AC10_Checklist_AppPrev_IsNeverCreated()
    {
        var violations = AppPrevViolations(Read("DEPLOY_CHECKLIST.md"));
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Fact]
    public void AC10_AppPrevCheck_FlagsCopy_AllowsRemoval()
    {
        var bad = AppPrevViolations("a\n  sudo cp -a /opt/multibot/app /opt/multibot/app.prev\n");
        Assert.Contains(":2:", Assert.Single(bad));
        Assert.Empty(AppPrevViolations("  sudo rm -rf /opt/multibot/app.prev\n"));
    }

    [Theory]
    [InlineData("/opt/multibot/releases")]
    [InlineData("multibot-update.timer")]
    [InlineData("/usr/local/sbin/multibot-update")]
    [InlineData("/etc/multibot/updater.env")]
    public void AC10_Checklist_DescribesAutoUpdate(string required) =>
        Assert.Contains(required, Read("DEPLOY_CHECKLIST.md"), StringComparison.Ordinal);
}
