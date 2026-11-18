namespace Bot.Platform.Tests.RepositoryHygiene;

using Map = Dictionary<string, object?>;

/// <summary>
/// 007 AC-1..AC-10: структурные тесты .github/workflows/*.yml (разбор YAML). Негативы — на встроенных образцах:
/// каждый получен одной правкой заведомо корректного образца и обязан давать нарушение нужного правила.
/// </summary>
public class WorkflowTests
{
    // ---------- эталонные образцы (проходят все правила) ----------

    private const string GoodCi = """
        name: ci
        on:
          pull_request:
            branches: [main]
          push:
            branches: [main]
          workflow_dispatch:
          workflow_call:
        permissions:
          contents: read
        concurrency:
          group: ci-${{ github.event_name }}-${{ github.ref }}
          cancel-in-progress: ${{ github.event_name == 'pull_request' }}
        defaults:
          run:
            shell: bash
        jobs:
          build:
            runs-on: ubuntu-24.04
            steps:
              - uses: actions/checkout@v6
                with:
                  persist-credentials: false
              - uses: actions/setup-dotnet@v5
                with:
                  global-json-file: global.json
              - uses: actions/setup-node@v6
                with:
                  node-version: '22'
                  cache: npm
              - name: backend
                run: |
                  dotnet build -warnaserror
                  dotnet test --nologo
                  dotnet format --verify-no-changes
              - name: webapp
                working-directory: webapp
                run: |
                  npm ci
                  npm run build
                  npm run test -- --run
                  npm run lint
        """;

    private const string GoodRelease = """
        name: release
        on:
          workflow_dispatch:
            inputs:
              version:
                type: string
                required: true
        permissions: {}
        concurrency:
          group: release
          cancel-in-progress: false
        jobs:
          guard:
            runs-on: ubuntu-24.04
            permissions: {}
            steps:
              - name: only main
                env:
                  REF: ${{ github.ref }}
                run: |
                  if [ "$REF" != "refs/heads/main" ]; then
                    echo "::error::Релиз запускается только из ветки main, получено: $REF"
                    exit 1
                  fi
          checks:
            needs: guard
            permissions:
              contents: read
            uses: ./.github/workflows/ci.yml
          build:
            needs: checks
            runs-on: ubuntu-24.04
            permissions: { contents: read }
            steps:
              - uses: actions/checkout@v6
                with:
                  persist-credentials: false
              - uses: actions/setup-dotnet@v5
                with:
                  global-json-file: global.json
              - uses: actions/setup-node@v6
                with:
                  node-version: '22'
              - name: publish
                env:
                  VERSION: ${{ inputs.version }}
                run: |
                  dotnet publish -r linux-x64 --self-contained
                  npm ci
              - uses: actions/upload-artifact@v6
                with:
                  name: release
                  path: multibot.tar.gz
          publish:
            needs: build
            runs-on: ubuntu-24.04
            environment: release
            permissions:
              contents: write
            env:
              GH_REPO: ${{ github.repository }}
            steps:
              - uses: actions/download-artifact@v7
                with:
                  name: release
              - name: create
                env:
                  GH_TOKEN: ${{ github.token }}
                run: |
                  gh release create "$VERSION" multibot.tar.gz --target "$GITHUB_SHA" --generate-notes --latest
        """;

    // Образец раздела команд CLAUDE.md (в формате реального файла) — для проверок на встроенных образцах.
    private const string SampleClaudeMd = """
        ## Команды
        - Backend: `dotnet build -warnaserror`, `dotnet test --nologo`, `dotnet format --verify-no-changes`
        - Frontend: `cd webapp && npm run build && npm run test -- --run && npm run lint`

        ## Другое
        - `echo не команда CI`
        """;

    // ---------- загрузка реальных файлов ----------

    private static string RealClaudeMd => File.ReadAllText(Path.Combine(GitRepo.Root, "CLAUDE.md"));

    private static string WorkflowsDir => Path.Combine(GitRepo.Root, ".github", "workflows");

    public static IEnumerable<(string File, Map Root)> Real()
    {
        var files = Directory.Exists(WorkflowsDir)
            ? Directory.GetFiles(WorkflowsDir, "*.yml").Concat(Directory.GetFiles(WorkflowsDir, "*.yaml")).Order().ToList()
            : [];
        return files.Select(f => (Path.GetFileName(f), WorkflowRules.Parse(File.ReadAllText(f))));
    }

    private static Map RealFile(string name) =>
        Real().SingleOrDefault(w => w.File == name).Root
        ?? throw new Xunit.Sdk.XunitException($"Не найден .github/workflows/{name}");

    private static void NoViolations(IEnumerable<string> violations)
    {
        var list = violations.ToList();
        Assert.True(list.Count == 0, string.Join(Environment.NewLine, list));
    }

    private static void Flagged(IReadOnlyCollection<string> violations, string expectedFragment)
    {
        Assert.NotEmpty(violations);
        Assert.Contains(violations, x => x.Contains(expectedFragment, StringComparison.Ordinal));
    }

    private static string Mutate(string sample, string from, string to)
    {
        Assert.Contains(from, sample, StringComparison.Ordinal); // правка должна реально сработать
        return sample.Replace(from, to, StringComparison.Ordinal);
    }

    private static Map P(string yaml) => WorkflowRules.Parse(yaml);

    private static IEnumerable<string> ForAll(Func<string, Map, List<string>> rule) =>
        Real().SelectMany(w => rule(w.File, w.Root));

    // ---------- инфраструктура ----------

    [Fact]
    public void Workflows_SetIsNonEmpty_AndContainsCiAndRelease()
    {
        var names = Real().Select(w => w.File).ToList();
        Assert.NotEmpty(names);
        Assert.Contains("ci.yml", names);
        Assert.Contains("release.yml", names);
    }

    [Fact]
    public void Samples_GoodCiAndRelease_PassTheirRules()
    {
        var ci = P(GoodCi);
        var rel = P(GoodRelease);
        NoViolations(new[] { WorkflowRules.Ac1, WorkflowRules.Ac2, WorkflowRules.Ac3, WorkflowRules.Ac4, WorkflowRules.Ac5,
            WorkflowRules.Ac7, WorkflowRules.Ac10 }.SelectMany(r => r("ci.yml", ci)).Concat(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, ci)));
        NoViolations(new[] { WorkflowRules.Ac1, WorkflowRules.Ac2, WorkflowRules.Ac3, WorkflowRules.Ac4, WorkflowRules.Ac5,
            WorkflowRules.Ac7, WorkflowRules.Ac8, WorkflowRules.Ac9, WorkflowRules.Ac10 }.SelectMany(r => r("release.yml", rel)));
        NoViolations(WorkflowRules.Ac7NoCollision(ci, rel));
    }

    [Fact]
    public void Yaml_OnKey_IsReadWhetherStringOrBoolean()
    {
        var asBool = P(GoodCi.Replace("\non:\n", "\ntrue:\n", StringComparison.Ordinal));
        NoViolations(WorkflowRules.Ac1("ci.yml", asBool));
    }

    // ---------- AC-1 ----------

    [Fact]
    public void AC1_CiWorkflow_HasExactlyRequiredTriggers() =>
        NoViolations(WorkflowRules.Ac1("ci.yml", RealFile("ci.yml")));

    [Fact]
    public void AC1_AllWorkflows_DoNotUsePullRequestTargetOrWorkflowRun() => NoViolations(ForAll(WorkflowRules.Ac1));

    [Fact]
    public void AC1_Sample_PullRequestTarget_Flagged() =>
        Flagged(WorkflowRules.Ac1("ci.yml",
            P(Mutate(GoodCi, "  workflow_call:", "  workflow_call:\n  pull_request_target:"))), "pull_request_target");

    [Fact]
    public void AC1_Sample_WorkflowRun_Flagged() =>
        Flagged(WorkflowRules.Ac1("release.yml",
            P(Mutate(GoodRelease, "  workflow_dispatch:", "  workflow_run:\n    workflows: [ci]\n  workflow_dispatch:"))), "workflow_run");

    [Fact]
    public void AC1_Sample_PushToAllBranches_Flagged() =>
        Flagged(WorkflowRules.Ac1("ci.yml", P(Mutate(GoodCi, "  push:\n    branches: [main]", "  push:\n    branches: ['**']"))),
            "on.push.branches");

    [Fact]
    public void AC1_Sample_MissingWorkflowCall_Flagged() =>
        Flagged(WorkflowRules.Ac1("ci.yml", P(Mutate(GoodCi, "  workflow_call:\n", ""))), "ожидалось ровно");

    // ---------- AC-2 ----------

    [Fact]
    public void AC2_AllWorkflows_PermissionsAreMinimalAndExplicit() => NoViolations(ForAll(WorkflowRules.Ac2));

    [Fact]
    public void AC2_Sample_NoTopLevelPermissions_Flagged() =>
        Flagged(WorkflowRules.Ac2("release.yml", P(Mutate(GoodRelease, "permissions: {}\nconcurrency", "concurrency"))),
            "нет permissions на верхнем уровне");

    [Fact]
    public void AC2_Sample_CiWithWriteAll_Flagged() =>
        Flagged(WorkflowRules.Ac2("ci.yml", P(Mutate(GoodCi, "permissions:\n  contents: read", "permissions: write-all"))),
            "ровно 'contents: read'");

    [Fact]
    public void AC2_Sample_TwoJobsWithContentsWrite_Flagged() =>
        Flagged(WorkflowRules.Ac2("release.yml", P(Mutate(GoodRelease, "permissions: { contents: read }", "permissions: { contents: write }"))),
            "ожидалось 1");

    [Fact]
    public void AC2_Sample_OtherWriteScope_Flagged() =>
        Flagged(WorkflowRules.Ac2("release.yml", P(Mutate(GoodRelease, "permissions: { contents: read }", "permissions: { contents: read, packages: write }"))),
            "packages: write");

    [Fact]
    public void AC2_Sample_ReleaseJobWithoutExplicitPermissions_Flagged() =>
        Flagged(WorkflowRules.Ac2("release.yml", P(Mutate(GoodRelease, "    permissions: { contents: read }\n", ""))),
            "без явного permissions");

    // ---------- AC-3 ----------

    [Fact]
    public void AC3_AllCheckoutSteps_DisablePersistCredentials()
    {
        // не пусто: иначе проверка ничего не проверяет
        Assert.Contains(Real(), w => w.File == "ci.yml");
        NoViolations(ForAll(WorkflowRules.Ac3));
    }

    [Fact]
    public void AC3_Sample_CheckoutWithoutPersistCredentials_Flagged() =>
        Flagged(WorkflowRules.Ac3("ci.yml", P(Mutate(GoodCi, "persist-credentials: false", "fetch-depth: 1"))),
            "persist-credentials");

    [Fact]
    public void AC3_Sample_PersistCredentialsTrue_Flagged() =>
        Flagged(WorkflowRules.Ac3("ci.yml", P(Mutate(GoodCi, "persist-credentials: false", "persist-credentials: true"))),
            "persist-credentials");

    // ---------- AC-4 ----------

    [Fact]
    public void AC4_AllUses_ArePinnedFirstPartyOrLocal() => NoViolations(ForAll(WorkflowRules.Ac4));

    [Theory]
    [InlineData("actions/checkout@main")]
    [InlineData("actions/checkout@master")]
    [InlineData("actions/checkout@latest")]
    [InlineData("actions/checkout@v6.1.0")]
    [InlineData("actions/checkout")]
    [InlineData("evil/checkout@v6")]
    [InlineData("docker://alpine:3")]
    public void AC4_Sample_DisallowedRef_Flagged(string uses) =>
        Flagged(WorkflowRules.Ac4("ci.yml", P(Mutate(GoodCi, "actions/checkout@v6", uses))), "недопустимая ссылка");

    [Fact]
    public void AC4_Sample_ShaAndActionlintAndLocalJob_Allowed()
    {
        var sha = new string('a', 40);
        var yaml = Mutate(GoodCi, "actions/checkout@v6", "actions/checkout@" + sha);
        yaml = Mutate(yaml, "  build:\n", "  lint:\n    uses: ./.github/workflows/x.yml\n  build:\n");
        NoViolations(WorkflowRules.Ac4("ci.yml", P(yaml)));
        NoViolations(WorkflowRules.Ac4("ci.yml", P(Mutate(GoodCi, "actions/checkout@v6", "rhysd/actionlint@v1"))));
    }

    [Fact]
    public void AC4_Sample_JobLevelThirdPartyUses_Flagged() =>
        Flagged(WorkflowRules.Ac4("release.yml", P(Mutate(GoodRelease, "uses: ./.github/workflows/ci.yml", "uses: someone/ci/.github/workflows/ci.yml@v1"))),
            "job 'checks'");

    // ---------- AC-5 ----------

    [Fact]
    public void AC5_Toolchain_UsesGlobalJsonAndSingleNodeVersion()
    {
        NoViolations(ForAll(WorkflowRules.Ac5));
        // непустота: setup-dotnet / setup-node реально есть в ci.yml
        var ci = File.ReadAllText(Path.Combine(WorkflowsDir, "ci.yml"));
        Assert.Contains("actions/setup-dotnet@", ci, StringComparison.Ordinal);
        Assert.Contains("actions/setup-node@", ci, StringComparison.Ordinal);
    }

    [Fact]
    public void AC5_AllSetupNodeSteps_ShareSameNodeVersion()
    {
        var versions = Real().SelectMany(w => w.Root.GetValueOrDefault("jobs") is Map jobs
            ? jobs.Values.OfType<Map>().SelectMany(j => j.TryGetValue("steps", out var s) && s is List<object?> l ? l.OfType<Map>() : [])
            : [])
            .Where(s => s.TryGetValue("uses", out var u) && u is string us && us.StartsWith("actions/setup-node@", StringComparison.Ordinal))
            .Select(s => (s.GetValueOrDefault("with") as Map)?.GetValueOrDefault("node-version") as string)
            .ToList();
        Assert.NotEmpty(versions);
        Assert.Single(versions.Distinct());
    }

    [Fact]
    public void AC5_Sample_DotnetVersionInsteadOfGlobalJson_Flagged() =>
        Flagged(WorkflowRules.Ac5("ci.yml", P(Mutate(GoodCi, "global-json-file: global.json", "dotnet-version: '10.0.x'"))),
            "global-json-file");

    [Fact]
    public void AC5_Sample_BothGlobalJsonAndDotnetVersion_Flagged() =>
        Flagged(WorkflowRules.Ac5("ci.yml", P(Mutate(GoodCi, "global-json-file: global.json", "global-json-file: global.json\n          dotnet-version: '10.0.x'"))),
            "с dotnet-version");

    [Fact]
    public void AC5_Sample_NodeVersionDiffers_Flagged() =>
        Flagged(WorkflowRules.Ac5("release.yml", P(Mutate(GoodRelease, "node-version: '22'", "node-version: '20'"))),
            "node-version");

    // ---------- AC-6 ----------

    [Fact]
    public void AC6_CiBuildJob_RunsProjectCommandsInOrderViaBash() =>
        NoViolations(WorkflowRules.Ac6("ci.yml", RealClaudeMd, RealFile("ci.yml")));

    [Fact]
    public void AC6_Sample_NoShellBash_Flagged() =>
        Flagged(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(GoodCi, "defaults:\n  run:\n    shell: bash\n", ""))), "shell");

    [Fact]
    public void AC6_Sample_ShellOnBuildJob_Allowed() =>
        NoViolations(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(
            Mutate(GoodCi, "defaults:\n  run:\n    shell: bash\n", ""),
            "    runs-on: ubuntu-24.04\n", "    runs-on: ubuntu-24.04\n    defaults:\n      run:\n        shell: bash\n"))));

    [Fact]
    public void AC6_Sample_TestBeforeBuild_Flagged() =>
        Flagged(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(GoodCi, "npm run build\n          npm run test -- --run", "npm run test -- --run\n          npm run build"))),
            "'npm run test -- --run'");

    [Fact]
    public void AC6_Sample_MissingFormatCheck_Flagged() =>
        Flagged(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(GoodCi, "          dotnet format --verify-no-changes\n", ""))),
            "dotnet format --verify-no-changes");

    [Fact]
    public void AC6_Sample_NpmOutsideWebapp_Flagged() =>
        Flagged(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(GoodCi, "        working-directory: webapp\n", ""))), "не в webapp");

    [Fact]
    public void AC6_Sample_CdWebappOnSameLine_Allowed() =>
        NoViolations(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(
            Mutate(GoodCi, "        working-directory: webapp\n", ""),
            "npm ci\n          npm run build\n          npm run test -- --run\n          npm run lint",
            "cd webapp && npm ci\n          cd webapp && npm run build\n          cd webapp && npm run test -- --run\n          cd webapp && npm run lint"))));

    [Fact]
    public void AC6_CommandsAreReadFromClaudeMd_NotHardcoded()
    {
        var commands = WorkflowRules.CiCommandsFrom(RealClaudeMd);
        Assert.NotEmpty(commands);
        Assert.Contains(("dotnet build -warnaserror", false), commands);
        Assert.Contains(("npm run lint", true), commands);
        // npm ci — единственная лишняя команда, строго перед первой npm
        var firstNpm = commands.FindIndex(c => c.Command.StartsWith("npm ", StringComparison.Ordinal));
        Assert.Equal("npm ci", commands[firstNpm].Command);
        Assert.Equal(1, commands.Count(c => c.Command == "npm ci"));
        Assert.DoesNotContain(commands, c => c.Command.Contains("echo", StringComparison.Ordinal));
    }

    [Fact]
    public void AC6_Sample_ClaudeMdHasCommandAbsentInYaml_Flagged()
    {
        var md = SampleClaudeMd.Replace("`dotnet test --nologo`", "`dotnet test --nologo`, `dotnet run --project tools/Check`", StringComparison.Ordinal);
        Assert.Contains("tools/Check", md, StringComparison.Ordinal);
        Flagged(WorkflowRules.Ac6("ci.yml", md, P(GoodCi)), "dotnet run --project tools/Check");
    }

    [Fact]
    public void AC6_Sample_ClaudeMdMatchesYaml_NoViolations() =>
        NoViolations(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(GoodCi)));

    [Theory]
    [InlineData("")]
    [InlineData("# Проект\n\nНет нужного раздела.\n")]
    [InlineData("## Команды\n\nТекст без команд в кавычках.\n\n## Дальше\n- `dotnet build`\n")]
    public void AC6_Sample_ClaudeMdWithoutParsableCommands_FailsClearly(string md)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WorkflowRules.Ac6("ci.yml", md, P(GoodCi)));
        Assert.Contains("CLAUDE.md", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AC6_Sample_NoBuildJob_Flagged() =>
        Flagged(WorkflowRules.Ac6("ci.yml", SampleClaudeMd, P(Mutate(GoodCi, "  build:\n", "  check:\n"))), "нет job с id 'build'");

    // ---------- AC-7 ----------

    [Fact]
    public void AC7_Ci_CancelsOnlyPullRequests_ReleaseNeverCancels()
    {
        NoViolations(WorkflowRules.Ac7("ci.yml", RealFile("ci.yml")));
        NoViolations(WorkflowRules.Ac7("release.yml", RealFile("release.yml")));
    }

    [Fact]
    public void AC7_CiAndReleaseGroups_NeverCollide() =>
        NoViolations(WorkflowRules.Ac7NoCollision(RealFile("ci.yml"), RealFile("release.yml")));

    [Fact]
    public void AC7_Sample_CancelAlways_Flagged() =>
        Flagged(WorkflowRules.Ac7("ci.yml", P(Mutate(GoodCi, "${{ github.event_name == 'pull_request' }}", "true"))), "cancel-in-progress");

    [Fact]
    public void AC7_Sample_CancelOnPushToo_Flagged() =>
        Flagged(WorkflowRules.Ac7("ci.yml", P(Mutate(GoodCi, "github.event_name == 'pull_request'", "github.event_name != 'workflow_dispatch'"))),
            "cancel-in-progress");

    [Fact]
    public void AC7_Sample_ReleaseCancelsInProgress_Flagged() =>
        Flagged(WorkflowRules.Ac7("release.yml", P(Mutate(GoodRelease, "cancel-in-progress: false", "cancel-in-progress: true"))),
            "cancel-in-progress должно быть false");

    [Fact]
    public void AC7_Sample_ReleaseGroupNotConstant_Flagged() =>
        Flagged(WorkflowRules.Ac7("release.yml", P(Mutate(GoodRelease, "group: release", "group: release-${{ github.ref }}"))),
            "константой");

    [Fact]
    public void AC7_Sample_CiGroupCanEqualReleaseGroup_Flagged() =>
        Flagged(WorkflowRules.Ac7NoCollision(
                P(Mutate(GoodCi, "group: ci-${{ github.event_name }}-${{ github.ref }}", "group: ${{ github.workflow }}")),
                P(GoodRelease)),
            "взаимная блокировка");

    [Fact]
    public void AC7_Sample_CiGroupEqualsReleaseConstant_Flagged() =>
        Flagged(WorkflowRules.Ac7NoCollision(
                P(Mutate(GoodCi, "group: ci-${{ github.event_name }}-${{ github.ref }}", "group: release")),
                P(GoodRelease)),
            "взаимная блокировка");

    // ---------- AC-8 ----------

    [Fact]
    public void AC8_Release_HasGuardEveryJobDependsOn_AndPublishUsesEnvironment() =>
        NoViolations(WorkflowRules.Ac8("release.yml", RealFile("release.yml")));

    [Fact]
    public void AC8_Sample_NoGuardJob_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P("""
            on: workflow_dispatch
            permissions: {}
            jobs:
              build:
                runs-on: ubuntu-24.04
                steps:
                  - run: echo build
              publish:
                needs: build
                runs-on: ubuntu-24.04
                environment: release
                permissions:
                  contents: write
                steps:
                  - run: gh release create
            """)), "не содержит сообщения");

    [Fact]
    public void AC8_Sample_GuardMessageChanged_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P(Mutate(GoodRelease, "Релиз запускается только из ветки main", "Only main"))),
            "не содержит сообщения");

    [Fact]
    public void AC8_Sample_GuardWithoutExit_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P(Mutate(GoodRelease, "exit 1", "true"))), "exit 1");

    [Fact]
    public void AC8_Sample_GuardDoesNotCheckRef_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P(Mutate(GoodRelease, "refs/heads/main", "refs/heads/dev"))), "refs/heads/main");

    [Fact]
    public void AC8_Sample_JobNotDependingOnGuard_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P(Mutate(GoodRelease, "  checks:\n    needs: guard\n", "  checks:\n"))),
            "job 'checks' не зависит");

    [Fact]
    public void AC8_Sample_GuardWithJobLevelIf_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P(Mutate(GoodRelease, "  guard:\n", "  guard:\n    if: github.ref == 'refs/heads/main'\n"))),
            "job-level if");

    [Fact]
    public void AC8_Sample_PublishWithoutEnvironment_Flagged() =>
        Flagged(WorkflowRules.Ac8("release.yml", P(Mutate(GoodRelease, "    environment: release\n", ""))),
            "environment: release");

    // ---------- AC-9 ----------

    [Fact]
    public void AC9_Release_SeparatesBuildFromPublish() =>
        NoViolations(WorkflowRules.Ac9("release.yml", RealFile("release.yml")));

    [Fact]
    public void AC9_Sample_PublishJobRunsNpmCi_Flagged() =>
        Flagged(WorkflowRules.Ac9("release.yml", P(Mutate(GoodRelease, "gh release create", "npm ci\n          gh release create"))),
            "выполняет checkout/npm ci/dotnet publish и имеет contents: write");

    [Fact]
    public void AC9_Sample_PublishJobChecksOutAndSetsUpNode_Flagged()
    {
        var yaml = Mutate(GoodRelease, "      - uses: actions/download-artifact@v7",
            "      - uses: actions/checkout@v6\n        with:\n          persist-credentials: false\n      - uses: actions/setup-node@v6\n        with:\n          node-version: '22'\n      - uses: actions/download-artifact@v7");
        var violations = WorkflowRules.Ac9("release.yml", P(yaml));
        Flagged(violations, "использует actions/setup-node");
        Flagged(violations, "contents: write");
    }

    [Fact]
    public void AC9_Sample_BuildJobWithContentsWrite_Flagged() =>
        Flagged(WorkflowRules.Ac9("release.yml", P(Mutate(GoodRelease, "permissions: { contents: read }", "permissions: { contents: write }"))),
            "job 'build'");

    [Fact]
    public void AC9_Sample_PublishWithoutDownloadArtifact_Flagged() =>
        Flagged(WorkflowRules.Ac9("release.yml", P(Mutate(GoodRelease, "actions/download-artifact@v7", "actions/upload-artifact@v6"))),
            "download-artifact");

    [Fact]
    public void AC9_Sample_PublishWithoutRepo_Flagged() =>
        Flagged(WorkflowRules.Ac9("release.yml", P(Mutate(GoodRelease, "GH_REPO:", "OTHER:"))), "GH_REPO / --repo");

    [Fact]
    public void AC9_Sample_SetupNodeWithCache_Flagged() =>
        Flagged(WorkflowRules.Ac9("release.yml", P(Mutate(GoodRelease, "node-version: '22'", "node-version: '22'\n          cache: npm"))),
            "setup-node с cache");

    [Theory]
    [InlineData("--target \"$GITHUB_SHA\"", true)]
    [InlineData("--target \"${GITHUB_SHA}\"", true)]
    [InlineData("--target ${{ github.sha }}", true)]
    [InlineData("--target main", false)]
    [InlineData("--target \"$GITHUB_REF_NAME\"", false)]
    [InlineData("--generate-notes", false)]
    public void AC9_PublishTarget_MustBeRunCommit(string targetArg, bool allowed)
    {
        var yaml = Mutate(GoodRelease, "--target \"$GITHUB_SHA\"", targetArg);
        var violations = WorkflowRules.Ac9("release.yml", P(yaml));
        if (allowed) NoViolations(violations);
        else Flagged(violations, "--target");
    }

    [Theory]
    [InlineData("dotnet publish -r linux-x64")]
    [InlineData("npm run build")]
    public void AC9_Sample_PublishJobWithoutCheckoutRunsDotnetOrNpm_Flagged(string command)
    {
        var yaml = Mutate(GoodRelease, "          gh release create", $"          {command}\n          gh release create");
        var violations = WorkflowRules.Ac9("release.yml", P(yaml));
        Flagged(violations, "job публикации 'publish' вызывает npm/dotnet");
    }

    // ---------- AC-10 ----------

    [Fact]
    public void AC10_AllRunScripts_HaveNoUntrustedExpressions()
    {
        Assert.NotEmpty(Real());
        NoViolations(ForAll(WorkflowRules.Ac10));
    }

    [Theory]
    [InlineData("${{ inputs.version }}")]
    [InlineData("${{inputs.version}}")]
    [InlineData("${{ github.event.pull_request.title }}")]
    [InlineData("${{ github.head_ref }}")]
    public void AC10_Sample_UntrustedExpressionInRun_Flagged(string expr) =>
        Flagged(WorkflowRules.Ac10("release.yml", P(Mutate(GoodRelease, "gh release create \"$VERSION\"", $"gh release create \"{expr}\""))),
            "внутри run");

    [Fact]
    public void AC10_Sample_ExpressionInEnv_Allowed() =>
        NoViolations(WorkflowRules.Ac10("release.yml", P(GoodRelease))); // inputs.version в env — норма

    [Fact]
    public void AC10_Sample_EventNameInRun_NotAnInjectionVector() =>
        NoViolations(WorkflowRules.Ac10("ci.yml", P(Mutate(GoodCi, "npm run lint", "echo ${{ github.event_name }}"))));
}
