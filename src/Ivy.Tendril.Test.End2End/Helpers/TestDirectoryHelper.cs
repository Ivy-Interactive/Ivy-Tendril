namespace Ivy.Tendril.Test.End2End.Helpers;

public static class TestDirectoryHelper
{
    public static readonly string[] DefaultStalePatterns =
    [
        "tendril-e2e-*",
        "tendril-e2e-repo-*",
        "tendril-pw-*",
        "ivy-agents-e2e*"
    ];

    public static void PurgeStaleTestDirectories(TimeSpan? maxAge = null)
    {
        var age = maxAge ?? TimeSpan.FromHours(1);
        foreach (var pattern in DefaultStalePatterns)
        {
            PurgeStaleDirectories(pattern, age);
        }
    }

    public static void PurgeStaleDirectories(string searchPattern, TimeSpan maxAge, string? basePath = null)
    {
        var baseDir = basePath ?? Path.GetTempPath();
        if (!Directory.Exists(baseDir)) return;

        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var dir in Directory.EnumerateDirectories(baseDir, searchPattern))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastWriteTimeUtc < cutoff && dirInfo.CreationTimeUtc < cutoff)
                    {
                        DeleteDirectorySafely(dir);
                    }
                }
                catch
                {
                    // Ignore individual directory enumeration / access errors
                }
            }
        }
        catch
        {
            // Ignore top-level enumeration errors
        }
    }

    public static async Task DeleteDirectorySafelyAsync(string path, int maxAttempts = 3)
    {
        if (!Directory.Exists(path)) return;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception) when (i < maxAttempts - 1)
            {
                await Task.Delay(500 * (i + 1));
            }
        }
    }

    public static void DeleteDirectorySafely(string path, int maxAttempts = 3)
    {
        if (!Directory.Exists(path)) return;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception) when (i < maxAttempts - 1)
            {
                Thread.Sleep(500 * (i + 1));
            }
        }
    }

    public static void ClearReadOnlyAttributes(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }
                catch { }
            }
        }
        catch { }
    }
}
