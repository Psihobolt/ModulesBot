namespace Bot.Platform.Tests.RepositoryHygiene;

/// <summary>AC-4/AC-5: сканер на образцах. Образцы R1–R4 собираются в рантайме (файл сам проходит AC-3).</summary>
public class SecretScannerTests
{
    private static string Telegram(int digits = 9, int tail = 35) =>
        new string('7', digits) + ":" + string.Concat(Enumerable.Repeat("Ab3_-", 10))[..tail];

    private static string Ctx7(int len = 36) => "ctx7sk" + "-" + new string('a', len - 4) + "0-1b";

    private static string Pem(string kind) => "-----" + "BEGIN " + kind + "PRIVATE KEY" + "-----";

    private static string Jwt() => "e" + "yJhbGciOiJIUzI1NiJ9" + "." + "e" + "yJzdWIiOiIxMjM0NTY3ODkwIn0" + "." + "SflKxwRJSMeKKF2QT4fw";

    private static string[] Rules(string path, string content) =>
        SecretScanner.Scan(path, content).Select(f => f.RuleId).ToArray();

    public static TheoryData<string, string> Positives => new()
    {
        { "R1", "token = " + Telegram() },
        { "R1", "x " + Telegram(8) + " y" },
        { "R1", Telegram(10) },
        { "R2", "key " + Ctx7() },
        { "R2", Ctx7(27) },
        { "R3", Pem("RSA ") },
        { "R3", Pem("") },
        { "R3", Pem("EC ") },
        { "R4", "Authorization: Bearer " + Jwt() },
    };

    [Theory]
    [MemberData(nameof(Positives))]
    public void AC4_RealSecretFormat_IsFlaggedInAnyFile(string rule, string line)
    {
        Assert.Contains(rule, Rules("docs/anything.md", line));
        Assert.Contains(rule, Rules("src/x.cs", line));
    }

    public static TheoryData<string, string, string> ConfigPositives => new()
    {
        { "a/appsettings.json", "{ \"Bot\": {\n  \"Token\": \"abc123\"\n} }", "R5" },
        { "a/appsettings.json", "\"WebhookSecret\": \"s3cr3t\"", "R5" },
        { "a/settings.json", "\"CONTEXT7_API_KEY\": \"anything\"", "R5" },
        { "a/x.json", "\"token\": \"lower\"", "R5" },
        { "a/x.json", "\"ApiKey\": \"k\"", "R5" },
        { "a/x.json", "\"Db:Password\": \"p\"", "R5" },
        { "a/x.json", "{\"Other\":1,\"Token\":\"minified\"}", "R5" },
        { "a/prod.env", "Bot__Token=abc", "R5" },
        { "a/prod.env", "Bot__WebhookSecret=abc", "R5" },
        { "a/prod.env.example", "Bot__Token=abc", "R5" },
        { "a/prod.env.example", "Bot__WebhookSecret=abc", "R5" },
        { ".env.local", "export API_KEY=abc", "R5" },
        { ".envrc", "export API_KEY=abc", "R5" },
        { "a/.envrc", "Bot__Token=abc", "R5" },
        { "a/c.yml", "Bot:\n  Token: abc", "R5" },
        { "a/c.ini", "Password = hunter2", "R5" },
        { "a/c.toml", "Secret = \"x\"", "R5" },
        { "a/c.conf", "ApiKey=1234", "R5" },
        { "a/prod.env", "Bot__Token=12345", "R5" },
    };

    [Theory]
    [MemberData(nameof(ConfigPositives))]
    public void AC4_AssignedSecretInConfig_IsFlagged(string path, string content, string rule) =>
        Assert.Contains(rule, Rules(path, content));

    public static TheoryData<string, string> Negatives => new()
    {
        { "x.cs", "123456:TEST-TOKEN" },
        { "x.cs", "654321:ANOTHER-BOT-TOKEN" },
        { "x.cs", "test-webhook-secret" },
        { "x.cs", "distinct-secret-value-xyz" },
        { "x.md", new string('a', 32) + new string('0', 32) },
        { "x.cs", Telegram(9, 36) },          // хвост длиннее 35 — по краю [A-Za-z0-9_-]
        { "x.cs", Telegram(9, 34) },          // короче 35
        { "x.cs", "k" + Telegram() },         // слева буква
        { "x.cs", Telegram(7) },              // 7 цифр
        { "x.cs", Telegram(11) },             // 11 цифр
        { "x.cs", "ctx7sk" + "-short" },
        { "x.cs", "-----BEGIN CERTIFICATE-----" },
        { "x.cs", "e" + "yJhbGciOiJIUzI1NiJ9" + "." + "e" + "yJzdWIiOiIxMjM0NTY3ODkwIn0" },  // два сегмента
        { "a/x.json", "\"CONTEXT7_API_KEY\": \"${CONTEXT7_API_KEY}\"" },
        { "a/x.json", "\"Token\": \"\"" },
        { "a/x.json", "\"Token\": \"<token>\"" },
        { "a/x.json", "\"Token\": null" },
        { "a/x.json", "\"Token\": 5" },
        { "a/x.json", "\"Secret\": true" },
        { "a/x.json", "\"Token\": {" },
        { "a/x.json", "\"Password\": [" },
        { "a/x.json", "\"TokenLifetime\": \"5\"" },
        { "a/x.json", "\"Bot:PublicUrl\": \"https://x\"" },
        { "a/prod.env", "Bot__Token=<токен>" },
        { "a/prod.env", "Bot__Token=" },
        { "a/prod.env", "Bot__Token=${BOT_TOKEN}" },
        { "a/prod.env", "Bot__PublicUrl=https://x" },
        { "a/package-lock.json", "\"node_modules/js-tokens\": {" },
        { "a/package-lock.json", "\"Token\": \"abc\"" },
        { "a/x.md", "\"Token\": \"abc123\"" },
        { "a/x.cs", "\"WebhookSecret\": \"s3cr3t\"" },
        { "a/x.ts", "Bot__Token=abc" },
        { "a/x.md", "Bot__Token=abc" },
    };

    [Theory]
    [MemberData(nameof(Negatives))]
    public void AC4_FakeOrPlaceholder_IsNotFlagged(string path, string content) =>
        Assert.Empty(SecretScanner.Scan(path, content));

    [Fact]
    public void AC4_Sample_ReportsCorrectLine()
    {
        var f = Assert.Single(SecretScanner.Scan("a/x.md", "one\ntwo\r\n" + Telegram() + "\n"));
        Assert.Equal(3, f.Line);
        Assert.Equal("R1", f.RuleId);
    }

    [Fact]
    public void AC4_Allowlist_SuppressesOnlyMatchingPathAndRule()
    {
        var findings = SecretScanner.Scan("a/x.md", Telegram())
            .Concat(SecretScanner.Scan("b/y.md", Telegram())).ToList();
        Assert.Equal(2, findings.Count);

        var left = SecretScanner.ApplyAllowlist(findings, [new AllowEntry("a/x.md", "R1", "test")]).ToList();
        Assert.Equal("b/y.md", Assert.Single(left).Path);

        var other = SecretScanner.ApplyAllowlist(findings, [new AllowEntry("a/x.md", "R2", "wrong rule")]).ToList();
        Assert.Equal(2, other.Count);
    }

    [Fact]
    public void AC4_BinaryFile_IsSkipped()
    {
        var dir = Helpers.TempDir.Create();
        try
        {
            var bytes = new byte[] { 1, 0, 2 }.Concat(System.Text.Encoding.UTF8.GetBytes(Telegram())).ToArray();
            File.WriteAllBytes(Path.Combine(dir, "bin.dat"), bytes);
            File.WriteAllText(Path.Combine(dir, "text.txt"), Telegram());
            Assert.Empty(SecretScanner.ScanFile(dir, "bin.dat"));
            Assert.Single(SecretScanner.ScanFile(dir, "text.txt"));
        }
        finally
        {
            Helpers.TempDir.TryDelete(dir);
        }
    }

    [Theory]
    [InlineData("R1")]
    [InlineData("R2")]
    [InlineData("R3")]
    [InlineData("R4")]
    [InlineData("R5")]
    public void AC5_Message_HasLocationAndRuleButNoSecretValue(string rule)
    {
        var (path, secretLine, secret) = rule switch
        {
            "R1" => ("cfg/a.json", "x = " + Telegram(9), Telegram(9)),
            "R2" => ("cfg/a.json", "x = " + Ctx7(), Ctx7()),
            "R3" => ("cfg/a.json", Pem("RSA "), Pem("RSA ")),
            "R4" => ("cfg/a.json", "b " + Jwt(), Jwt()),
            _ => ("cfg/a.json", "\"Token\": \"zq9!unusualvalue\"", "zq9!unusualvalue"),
        };
        var findings = SecretScanner.Scan(path, "line1\n" + secretLine);
        var finding = Assert.Single(findings, f => f.RuleId == rule);

        var message = finding.ToString();
        Assert.Contains("cfg/a.json:2", message);
        Assert.Contains(rule, message);
        for (var i = 0; i + 5 <= secret.Length; i++)
        {
            Assert.DoesNotContain(secret.Substring(i, 5), message);
        }
    }
}
