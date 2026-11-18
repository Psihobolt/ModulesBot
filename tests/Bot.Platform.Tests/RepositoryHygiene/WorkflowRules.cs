using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Bot.Platform.Tests.RepositoryHygiene;

using Map = Dictionary<string, object?>;
using Seq = List<object?>;

/// <summary>
/// Структурные правила 007 (AC-1..AC-10) над разобранным YAML workflow. Каждое правило возвращает список нарушений
/// (пусто = ок); сообщения называют файл, job и шаг. YAML разбирается без типизации скаляров: ключ <c>on</c> остаётся
/// строкой "on" (в YAML 1.1 это булево true; на всякий случай поддержан и ключ "true").
/// </summary>
internal static partial class WorkflowRules
{
    public const string ReleaseFile = "release.yml";
    public const string CiFile = "ci.yml";

    // ---------- разбор ----------

    public static Map Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return Convert(stream.Documents[0].RootNode) as Map
               ?? throw new InvalidOperationException("Корень workflow — не mapping.");
    }

    private static object? Convert(YamlNode node) => node switch
    {
        YamlScalarNode s => s.Value ?? string.Empty,
        YamlSequenceNode q => q.Children.Select(Convert).ToList<object?>(),
        YamlMappingNode m => m.Children.ToDictionary(
            kv => ((YamlScalarNode)kv.Key).Value ?? string.Empty,
            kv => Convert(kv.Value)),
        _ => null,
    };

    // ---------- доступ ----------

    private static object? Get(object? node, string key) =>
        node is Map m && m.TryGetValue(key, out var v) ? v : null;

    private static string? Str(object? node) => node as string;

    private static Map Jobs(Map root) => Get(root, "jobs") as Map ?? [];

    private static IEnumerable<Map> Steps(object? job) =>
        (Get(job, "steps") as Seq ?? []).OfType<Map>();

    private static object? On(Map root) => Get(root, "on") ?? Get(root, "true") ?? Get(root, "True");

    private static List<string> OnKeys(Map root) => On(root) switch
    {
        Map m => [.. m.Keys],
        Seq q => [.. q.OfType<string>()],
        string s => [s],
        _ => [],
    };

    private static string StepName(Map step, int index) =>
        Str(Get(step, "name")) ?? Str(Get(step, "uses")) ?? $"#{index + 1}";

    private static IEnumerable<(string Job, object? JobNode, int Index, Map Step)> AllSteps(Map root) =>
        Jobs(root).SelectMany(j => Steps(j.Value).Select((s, i) => (j.Key, j.Value, i, s)));

    private static IEnumerable<string> Strings(object? node) => node switch
    {
        string s => [s],
        Map m => m.Values.SelectMany(Strings),
        Seq q => q.SelectMany(Strings),
        _ => [],
    };

    private static string Where(string file, string job, Map step, int index) =>
        $"{file}: job '{job}', step '{StepName(step, index)}'";

    private static bool IsWrite(object? v) => Str(v) is "write" or "write-all";

    private static bool JobHasContentsWrite(object? job) => Get(job, "permissions") switch
    {
        string s => s == "write-all",
        Map m => Str(Get(m, "contents")) == "write",
        _ => false,
    };

    private static bool IsPlainJob(object? job) => Get(job, "steps") is not null;

    // ---------- AC-1 ----------

    public static List<string> Ac1(string file, Map root)
    {
        var v = new List<string>();
        var keys = OnKeys(root);
        foreach (var banned in new[] { "pull_request_target", "workflow_run" })
        {
            if (keys.Contains(banned)) v.Add($"{file}: триггер '{banned}' запрещён");
        }

        if (file != CiFile) return v;

        var expected = new[] { "pull_request", "push", "workflow_dispatch", "workflow_call" };
        if (!keys.Order().SequenceEqual(expected.Order()))
        {
            v.Add($"{file}: on = [{string.Join(", ", keys)}], ожидалось ровно [{string.Join(", ", expected)}]");
        }

        foreach (var trigger in new[] { "pull_request", "push" })
        {
            var branches = Get(Get(On(root), trigger), "branches") as Seq;
            if (branches is null || !branches.OfType<string>().SequenceEqual(["main"]))
            {
                v.Add($"{file}: on.{trigger}.branches должно быть ровно [main]");
            }
        }

        return v;
    }

    // ---------- AC-2 ----------

    public static List<string> Ac2(string file, Map root)
    {
        var v = new List<string>();
        var top = Get(root, "permissions");
        if (top is null)
        {
            v.Add($"{file}: нет permissions на верхнем уровне");
        }
        else if (file == CiFile)
        {
            if (top is not Map m || m.Count != 1 || Str(Get(m, "contents")) != "read")
            {
                v.Add($"{file}: permissions верхнего уровня должны быть ровно 'contents: read'");
            }
        }
        else if (file == ReleaseFile && (top is not Map m2 || m2.Count != 0))
        {
            v.Add($"{file}: permissions верхнего уровня должны быть пустыми ({{}})");
        }

        if (JobHasContentsWrite(root)) v.Add($"{file}: contents: write на уровне workflow");

        var writeJobs = 0;
        foreach (var (name, job) in Jobs(root))
        {
            var perms = Get(job, "permissions");
            if (file == ReleaseFile && perms is null)
            {
                v.Add($"{file}: job '{name}' без явного permissions");
            }

            if (JobHasContentsWrite(job)) writeJobs++;

            var scopes = perms switch
            {
                string s => [("*", s)],
                Map m => m.Select(kv => (kv.Key, Str(kv.Value) ?? string.Empty)).ToList(),
                _ => [],
            };
            foreach (var (scope, value) in scopes)
            {
                if (IsWrite(value) && !(scope == "contents" && value == "write"))
                {
                    v.Add($"{file}: job '{name}': недопустимое право {scope}: {value}");
                }
            }
        }

        var expectedWriters = file == ReleaseFile ? 1 : 0;
        if (writeJobs != expectedWriters)
        {
            v.Add($"{file}: jobs с contents: write = {writeJobs}, ожидалось {expectedWriters}");
        }

        return v;
    }

    // ---------- AC-3 ----------

    public static List<string> Ac3(string file, Map root)
    {
        var v = new List<string>();
        foreach (var (job, _, i, step) in AllSteps(root))
        {
            if (Str(Get(step, "uses"))?.StartsWith("actions/checkout@", StringComparison.Ordinal) != true) continue;
            if (Str(Get(Get(step, "with"), "persist-credentials")) != "false")
            {
                v.Add($"{Where(file, job, step, i)}: checkout без with.persist-credentials: false");
            }
        }

        return v;
    }

    // ---------- AC-4 ----------

    [GeneratedRegex(@"^(\./\.github/workflows/[\w.\-]+\.ya?ml|(actions/[\w.\-]+|rhysd/actionlint)@(v\d+|[0-9a-f]{40}))$")]
    private static partial Regex AllowedUses();

    public static List<string> Ac4(string file, Map root)
    {
        var v = new List<string>();
        foreach (var (name, job) in Jobs(root))
        {
            var uses = Str(Get(job, "uses"));
            if (uses is not null && !AllowedUses().IsMatch(uses))
            {
                v.Add($"{file}: job '{name}': недопустимая ссылка uses '{uses}'");
            }
        }

        foreach (var (job, _, i, step) in AllSteps(root))
        {
            var uses = Str(Get(step, "uses"));
            if (uses is not null && !AllowedUses().IsMatch(uses))
            {
                v.Add($"{Where(file, job, step, i)}: недопустимая ссылка uses '{uses}'");
            }
        }

        return v;
    }

    // ---------- AC-5 ----------

    public static List<string> Ac5(string file, Map root)
    {
        var v = new List<string>();
        foreach (var (job, _, i, step) in AllSteps(root))
        {
            var uses = Str(Get(step, "uses")) ?? string.Empty;
            var with = Get(step, "with");
            if (uses.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal))
            {
                if (Str(Get(with, "global-json-file")) != "global.json")
                    v.Add($"{Where(file, job, step, i)}: setup-dotnet без global-json-file: global.json");
                if (Get(with, "dotnet-version") is not null)
                    v.Add($"{Where(file, job, step, i)}: setup-dotnet с dotnet-version");
            }

            if (uses.StartsWith("actions/setup-node@", StringComparison.Ordinal)
                && Str(Get(with, "node-version")) != "22")
            {
                v.Add($"{Where(file, job, step, i)}: node-version = '{Str(Get(with, "node-version"))}', ожидалось '22'");
            }
        }

        return v;
    }

    // ---------- AC-6 ----------

    /// <summary>
    /// Команды из раздела «## Команды» CLAUDE.md: спаны в обратных кавычках, разбиение по <c>&amp;&amp;</c>,
    /// <c>cd webapp</c> переводит последующие команды спана в webapp. <c>npm ci</c> — единственная добавляемая
    /// команда (установка зависимостей перед первой npm-командой). Пустой/неразобранный раздел — исключение.
    /// </summary>
    public static List<(string Command, bool InWebapp)> CiCommandsFrom(string claudeMd)
    {
        var lines = claudeMd.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "## Команды");
        if (start < 0) throw new InvalidOperationException("CLAUDE.md: нет раздела '## Команды'.");

        var section = lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal));
        var result = new List<(string, bool)>();
        foreach (Match span in BacktickSpan().Matches(string.Join("\n", section)))
        {
            var inWebapp = false;
            foreach (var part in span.Groups[1].Value.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "cd webapp") { inWebapp = true; continue; }
                result.Add((part, inWebapp));
            }
        }

        if (result.Count == 0)
            throw new InvalidOperationException("CLAUDE.md: раздел '## Команды' не содержит ни одной команды в обратных кавычках.");

        var firstNpm = result.FindIndex(c => c.Item1.StartsWith("npm ", StringComparison.Ordinal));
        if (firstNpm >= 0) result.Insert(firstNpm, ("npm ci", true));
        return result;
    }

    [GeneratedRegex("`([^`\\n]+)`")]
    private static partial Regex BacktickSpan();

    [GeneratedRegex(@"^cd webapp\s*(&&|;)\s*")]
    private static partial Regex CdWebapp();

    public static List<string> Ac6(string file, string claudeMd, Map root)
    {
        var v = new List<string>();
        var ciCommands = CiCommandsFrom(claudeMd);
        if (Get(Jobs(root), "build") is not { } build)
        {
            v.Add($"{file}: нет job с id 'build'");
            return v;
        }

        var shell = Str(Get(Get(Get(root, "defaults"), "run"), "shell"))
                    ?? Str(Get(Get(Get(build, "defaults"), "run"), "shell"));
        if (shell != "bash") v.Add($"{file}: defaults.run.shell (workflow или job build) должен быть 'bash', получено '{shell}'");

        var lines = new List<(string Line, bool InWebapp, Map Step)>();
        foreach (var step in Steps(build))
        {
            var run = Str(Get(step, "run"));
            if (run is null) continue;
            var wd = Str(Get(step, "working-directory"));
            foreach (var raw in run.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var cd = CdWebapp().IsMatch(line);
                lines.Add((CdWebapp().Replace(line, string.Empty), cd || wd is "webapp" or "./webapp", step));
            }
        }

        var pos = -1;
        foreach (var (cmd, mustBeInWebapp) in ciCommands)
        {
            var idx = lines.FindIndex(pos + 1, l => l.Line == cmd);
            if (idx < 0)
            {
                v.Add($"{file}: job 'build': команда '{cmd}' не найдена после предыдущей (нужен порядок CLAUDE.md «Команды»)");
                continue;
            }

            pos = idx;
            if (mustBeInWebapp && !lines[idx].InWebapp)
            {
                v.Add($"{file}: job 'build': '{cmd}' выполняется не в webapp");
            }
        }

        return v;
    }

    // ---------- AC-7 ----------

    [GeneratedRegex(@"^\$\{\{\s*github\.event_name\s*==\s*'pull_request'\s*\}\}$")]
    private static partial Regex CancelOnlyForPr();

    public static List<string> Ac7(string file, Map root)
    {
        var v = new List<string>();
        var conc = Get(root, "concurrency");
        var group = Str(Get(conc, "group"));
        var cancel = Str(Get(conc, "cancel-in-progress"));
        if (group is null) v.Add($"{file}: нет concurrency.group");

        if (file == CiFile && cancel is not null && !CancelOnlyForPr().IsMatch(cancel.Trim()))
        {
            v.Add($"{file}: cancel-in-progress должно быть выражением \"github.event_name == 'pull_request'\", получено '{cancel}'");
        }

        if (file == CiFile && cancel is null) v.Add($"{file}: нет concurrency.cancel-in-progress");

        if (file == ReleaseFile)
        {
            if (group is not null && group.Contains("${{", StringComparison.Ordinal))
                v.Add($"{file}: concurrency.group должна быть константой, получено '{group}'");
            if (cancel != "false") v.Add($"{file}: cancel-in-progress должно быть false, получено '{cancel}'");
        }

        return v;
    }

    /// <summary>Инвариант: группа CI не может совпасть с константной группой релиза ни при каком значении выражений.</summary>
    public static List<string> Ac7NoCollision(Map ci, Map release)
    {
        var v = new List<string>();
        var ciGroup = Str(Get(Get(ci, "concurrency"), "group"));
        var relGroup = Str(Get(Get(release, "concurrency"), "group"));
        if (ciGroup is null || relGroup is null)
        {
            v.Add("нет concurrency.group в ci.yml или release.yml");
            return v;
        }

        var exprAt = ciGroup.IndexOf("${{", StringComparison.Ordinal);
        var prefix = exprAt < 0 ? ciGroup : ciGroup[..exprAt];
        var collides = exprAt < 0 ? ciGroup == relGroup : relGroup.StartsWith(prefix, StringComparison.Ordinal);
        if (collides)
        {
            v.Add($"группа CI '{ciGroup}' может совпасть с группой релиза '{relGroup}' (взаимная блокировка)");
        }

        return v;
    }

    // ---------- AC-8 ----------

    private const string GuardMessage = "::error::Релиз запускается только из ветки main, получено:";

    private static IEnumerable<string> Needs(object? job) => Get(job, "needs") switch
    {
        string s => [s],
        Seq q => q.OfType<string>(),
        _ => [],
    };

    public static List<string> Ac8(string file, Map root)
    {
        var v = new List<string>();
        var jobs = Jobs(root);
        if (jobs.Count == 0)
        {
            v.Add($"{file}: нет jobs");
            return v;
        }

        var (guardName, guard) = jobs.First();
        var runs = string.Join("\n", Steps(guard).Select(s => Str(Get(s, "run")) ?? string.Empty));
        var all = Strings(guard).ToList();
        if (!runs.Contains(GuardMessage, StringComparison.Ordinal))
            v.Add($"{file}: первый job '{guardName}' не содержит сообщения '{GuardMessage} <ref>'");
        if (!runs.Contains("exit 1", StringComparison.Ordinal))
            v.Add($"{file}: guard '{guardName}' не завершается 'exit 1'");
        if (!all.Any(s => s.Contains("github.ref", StringComparison.Ordinal))
            || !all.Any(s => s.Contains("refs/heads/main", StringComparison.Ordinal)))
        {
            v.Add($"{file}: guard '{guardName}' не сравнивает github.ref с refs/heads/main");
        }

        if (Get(guard, "if") is not null)
            v.Add($"{file}: guard '{guardName}' с job-level if (run будет skipped, а не failure)");

        // транзитивная зависимость от guard
        bool DependsOnGuard(string name, HashSet<string> seen)
        {
            if (!seen.Add(name) || !jobs.TryGetValue(name, out var j)) return false;
            return Needs(j).Any(n => n == guardName || DependsOnGuard(n, seen));
        }

        foreach (var name in jobs.Keys.Skip(1))
        {
            if (!DependsOnGuard(name, [])) v.Add($"{file}: job '{name}' не зависит (needs) от guard '{guardName}'");
        }

        var publishers = jobs.Where(j => JobHasContentsWrite(j.Value)).ToList();
        foreach (var (name, job) in publishers)
        {
            var env = Get(job, "environment");
            var envName = env as string ?? Str(Get(env, "name"));
            if (envName != "release") v.Add($"{file}: job публикации '{name}' без environment: release");
        }

        return v;
    }

    // ---------- AC-9 ----------

    [GeneratedRegex(@"\b(npm|dotnet)\b")]
    private static partial Regex NpmOrDotnet();

    [GeneratedRegex(@"--target[ =]+""?\$\{?(\w+)\}?""?|--target[ =]+(\$\{\{\s*github\.sha\s*\}\})")]
    private static partial Regex Target();

    public static List<string> Ac9(string file, Map root)
    {
        var v = new List<string>();
        foreach (var (name, job) in Jobs(root))
        {
            if (!IsPlainJob(job)) continue;
            var steps = Steps(job).ToList();
            var runs = string.Join("\n", steps.Select(s => Str(Get(s, "run")) ?? string.Empty));
            var usesList = steps.Select(s => Str(Get(s, "uses")) ?? string.Empty).ToList();
            var write = JobHasContentsWrite(job);

            var risky = runs.Contains("npm ci", StringComparison.Ordinal)
                        || runs.Contains("dotnet publish", StringComparison.Ordinal)
                        || usesList.Any(u => u.StartsWith("actions/checkout@", StringComparison.Ordinal));
            if (risky && write) v.Add($"{file}: job '{name}' выполняет checkout/npm ci/dotnet publish и имеет contents: write");

            if (write)
            {
                foreach (var u in usesList.Where(u => u.StartsWith("actions/checkout@", StringComparison.Ordinal)
                                                      || u.StartsWith("actions/setup-node@", StringComparison.Ordinal)
                                                      || u.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal)))
                {
                    v.Add($"{file}: job публикации '{name}' использует {u}");
                }

                if (NpmOrDotnet().IsMatch(runs)) v.Add($"{file}: job публикации '{name}' вызывает npm/dotnet");
                if (!usesList.Any(u => u.StartsWith("actions/download-artifact@", StringComparison.Ordinal)))
                    v.Add($"{file}: job публикации '{name}' не получает архив через actions/download-artifact");
                if (!runs.Contains("gh release create", StringComparison.Ordinal))
                    v.Add($"{file}: job публикации '{name}' не вызывает gh release create");

                var hasRepo = runs.Contains("--repo", StringComparison.Ordinal)
                              || Get(Get(job, "env"), "GH_REPO") is not null
                              || steps.Any(s => Get(Get(s, "env"), "GH_REPO") is not null);
                if (!hasRepo) v.Add($"{file}: job публикации '{name}': репозиторий не задан (GH_REPO / --repo)");

                var m = Target().Match(runs);
                var ok = false;
                if (m.Success)
                {
                    if (m.Groups[2].Success) ok = true;
                    else
                    {
                        var variable = m.Groups[1].Value;
                        ok = variable == "GITHUB_SHA"
                             || new[] { Get(job, "env") }.Concat(steps.Select(s => Get(s, "env")))
                                 .Concat([Get(root, "env")])
                                 .Any(e => Str(Get(e, variable))?.Contains("github.sha", StringComparison.Ordinal) == true);
                    }
                }

                if (!ok) v.Add($"{file}: job публикации '{name}': --target должен быть GITHUB_SHA этого run");
            }
        }

        foreach (var (job, _, i, step) in AllSteps(root))
        {
            if (file == ReleaseFile
                && Str(Get(step, "uses"))?.StartsWith("actions/setup-node@", StringComparison.Ordinal) == true
                && Get(Get(step, "with"), "cache") is not null)
            {
                v.Add($"{Where(file, job, step, i)}: setup-node с cache в релизе");
            }
        }

        return v;
    }

    // ---------- AC-10 ----------

    [GeneratedRegex(@"\$\{\{\s*(inputs\.|github\.event\.|github\.head_ref)")]
    private static partial Regex Injection();

    public static List<string> Ac10(string file, Map root)
    {
        var v = new List<string>();
        foreach (var (job, _, i, step) in AllSteps(root))
        {
            var run = Str(Get(step, "run"));
            if (run is not null && Injection().Match(run) is { Success: true } m)
            {
                v.Add($"{Where(file, job, step, i)}: выражение '{m.Value}…}}}}' внутри run (передавайте через env)");
            }
        }

        return v;
    }
}
