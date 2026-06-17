using System.Text.Json;

namespace Ivy.Tendril.Helpers;

public static class JobStatusFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void AppendCliInvocationDirect(string logPath, string command, int exitCode, double durationMs)
    {
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                FileHelper.EnsureDirectory(dir);
            var entry = new CliLogEntry(DateTime.UtcNow.ToString("O"), command, exitCode, durationMs);
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(logPath, line + "\n");
        }
        catch { /* Best-effort */ }
    }

    public record CliLogEntry(string Timestamp, string Command, int ExitCode, double DurationMs);
}
