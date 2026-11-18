using System.Text.RegularExpressions;

namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>Находка. Само совпавшее значение намеренно НЕ хранится (маскирование, AC-5).</summary>
internal sealed record SecretFinding(string Path, int Line, string RuleId)
{
    public override string ToString() => $"{Path}:{Line} [{RuleId}]";
}

/// <summary>Запись allowlist: (путь, id правила, причина).</summary>
internal sealed record AllowEntry(string Path, string RuleId, string Reason);

/// <summary>Правила R1–R5 из docs/tasks/006: ищутся форматы реальных секретов, а не слова token/secret.</summary>
internal static partial class SecretScanner
{
    // Регексы собраны из частей: исходник сканера сам не должен совпадать с R1–R4.
    private const string Boundary = "[A-Za-z0-9_-]";

    private static readonly Regex R1 = new(
        $"(?<!{Boundary})[0-9]{{8,10}}:{Boundary}{{35}}(?!{Boundary})", RegexOptions.CultureInvariant);

    private static readonly Regex R2 = new("ctx7sk" + "-[A-Za-z0-9-]{20,}", RegexOptions.CultureInvariant);

    private static readonly Regex R3 = new("-----BEGIN " + "[A-Z ]*" + "PRIVATE KEY" + "-----", RegexOptions.CultureInvariant);

    private static readonly Regex R4 = new(
        "ey" + "J[A-Za-z0-9_-]{7,}\\.ey" + "J[A-Za-z0-9_-]{7,}\\.[A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant);

    // "Ключ": значение — JSON / YAML с кавычками (может быть несколько пар на строке).
    private static readonly Regex QuotedPair = new(
        "\"(?<k>[^\"\\\\]+)\"\\s*[:=]\\s*(?<v>\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\]\\s]*)", RegexOptions.CultureInvariant);

    // KEY=value / key: value / key = value в начале строки (env, ini, conf, yaml, toml).
    private static readonly Regex BarePair = new(
        "^\\s*(?:export\\s+)?(?<k>[A-Za-z_][A-Za-z0-9_.:\\-]*)\\s*[:=]\\s*(?<v>.*)$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SecretKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Token", "Secret", "WebhookSecret", "ApiKey", "API_KEY", "CONTEXT7_API_KEY", "Password",
    };

    private static readonly string[] ConfigExtensions = [".json", ".ini", ".conf", ".yml", ".yaml", ".toml"];

    private static readonly string[] TypedExtensions = [".json", ".yml", ".yaml", ".toml"];

    public static IReadOnlyList<SecretFinding> Scan(string relativePath, string content)
    {
        var path = relativePath.Replace('\\', '/');
        var findings = new List<SecretFinding>();
        var isConfig = IsConfigFile(path);
        var typed = IsTypedFormat(path);
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var no = i + 1;
            if (R1.IsMatch(line)) findings.Add(new SecretFinding(path, no, "R1"));
            if (R2.IsMatch(line)) findings.Add(new SecretFinding(path, no, "R2"));
            if (R3.IsMatch(line)) findings.Add(new SecretFinding(path, no, "R3"));
            if (R4.IsMatch(line)) findings.Add(new SecretFinding(path, no, "R4"));
            if (isConfig && HasAssignedSecret(line, typed)) findings.Add(new SecretFinding(path, no, "R5"));
        }

        return findings;
    }

    /// <summary>Читает файл; бинарные (нулевой байт в первых 8 КБ) пропускает (возвращает пусто).</summary>
    public static IReadOnlyList<SecretFinding> ScanFile(string root, string relativePath)
    {
        var full = Path.Combine(root, relativePath);
        var bytes = File.ReadAllBytes(full);
        var head = Math.Min(bytes.Length, 8192);
        if (Array.IndexOf(bytes, (byte)0, 0, head) >= 0)
        {
            return [];
        }

        return Scan(relativePath, System.Text.Encoding.UTF8.GetString(bytes));
    }

    public static IEnumerable<SecretFinding> ApplyAllowlist(IEnumerable<SecretFinding> findings, IReadOnlyList<AllowEntry> allowlist) =>
        findings.Where(f => !allowlist.Any(a =>
            string.Equals(a.Path, f.Path, StringComparison.Ordinal) && string.Equals(a.RuleId, f.RuleId, StringComparison.Ordinal)));

    private static bool IsConfigFile(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith(".env", StringComparison.Ordinal) || name.EndsWith(".env", StringComparison.Ordinal)
            || name.EndsWith(".env.example", StringComparison.Ordinal))
        {
            return true;
        }

        return ConfigExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTypedFormat(string path) =>
        TypedExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static bool HasAssignedSecret(string line, bool typed)
    {
        foreach (Match m in QuotedPair.Matches(line))
        {
            if (IsSecretAssignment(m.Groups["k"].Value, m.Groups["v"].Value, typed)) return true;
        }

        var bare = BarePair.Match(line);
        return bare.Success && IsSecretAssignment(bare.Groups["k"].Value, bare.Groups["v"].Value, typed);
    }

    private static bool IsSecretAssignment(string key, string rawValue, bool typed)
    {
        var last = key.Split([":", "__", "."], StringSplitOptions.None)[^1];
        if (!SecretKeyNames.Contains(last)) return false;

        var value = rawValue.Trim().TrimEnd(',').Trim();
        var quoted = false;
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
        {
            value = value[1..^1];
            quoted = true;
        }

        if (value.Length == 0) return false;
        if (IsPlaceholder(value)) return false;
        if (!quoted)
        {
            if (value[0] is '{' or '[' or '"' or '\'') return false;
            if (typed && (decimal.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("null", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlaceholder(string v) =>
        (v.StartsWith('<') && v.EndsWith('>') && v.Length >= 2) || (v.StartsWith("${", StringComparison.Ordinal) && v.EndsWith('}'));
}
