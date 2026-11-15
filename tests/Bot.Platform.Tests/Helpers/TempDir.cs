namespace Bot.Platform.Tests.Helpers;

internal static class TempDir
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "bot-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
