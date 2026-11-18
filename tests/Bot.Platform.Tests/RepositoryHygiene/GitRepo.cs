using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Bot.Platform.Tests.RepositoryHygiene;

internal sealed record GitResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Запуск git в корне репозитория. Глобальные настройки пользователя нейтрализованы: пустой core.excludesFile,
/// пустой GIT_CONFIG_GLOBAL, без системного конфига и без XDG-игнора (~/.config/git/ignore).
/// Нет git или корня репозитория — исключение (тест падает, а не молча пропускается).
/// </summary>
internal static class GitRepo
{
    private static readonly Lazy<string> EmptyFile = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "bot-platform-tests", "git-empty");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "empty");
        if (!File.Exists(file)) File.WriteAllText(file, string.Empty); // идемпотентно при параллельных прогонах
        return file;
    });

    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var hasGlobalJson = File.Exists(Path.Combine(dir.FullName, "global.json"));
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (hasGlobalJson && (Directory.Exists(gitPath) || File.Exists(gitPath)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Корень репозитория не найден (вверх от '{AppContext.BaseDirectory}' нужен каталог с global.json и .git). " +
            "Проверка секретов не может быть пропущена.");
    }

    public static GitResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.excludesFile=" + EmptyFile.Value);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["GIT_CONFIG_GLOBAL"] = EmptyFile.Value;
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["XDG_CONFIG_HOME"] = Path.GetDirectoryName(EmptyFile.Value)!;
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("git не запустился.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "git не найден в PATH: проверка секретов/.gitignore не может быть выполнена (пропуск недопустим).", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new GitResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }

    /// <summary>git check-ignore --no-index: true — игнорируется, false — нет. Прочие коды — исключение.</summary>
    public static bool IsIgnored(string relativePath)
    {
        var r = Run("check-ignore", "--no-index", "-q", "--", relativePath);
        return r.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException(
                $"git check-ignore завершился с кодом {r.ExitCode} для '{relativePath}': {r.StdErr}"),
        };
    }

    /// <summary>Вывод git с -z, разобранный в список путей.</summary>
    public static IReadOnlyList<string> ListZ(params string[] args)
    {
        var r = Run([.. args, "-z"]);
        if (r.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} завершился с кодом {r.ExitCode}: {r.StdErr}");
        }

        return r.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
