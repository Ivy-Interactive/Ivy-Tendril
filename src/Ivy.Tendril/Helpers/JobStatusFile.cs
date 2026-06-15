using System.Text.Json;

namespace Ivy.Tendril.Helpers;

public static class JobStatusFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetCliLogPath(string jobId)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        return Path.Combine(tendrilHome, "Jobs", $"{jobId}.cli.jsonl");
    }

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

    public static void MoveLogToPlanFolder(string logPath, string planFolder, string jobType, string? jobId = null)
    {
        try
        {
            if (!File.Exists(logPath)) return;

            var logsDir = Path.Combine(planFolder, "Logs");
            FileHelper.EnsureDirectory(logsDir);

            var filename = !string.IsNullOrEmpty(jobId)
                ? $"{jobId}-{jobType}.cli.jsonl"
                : $"{jobType}.cli.jsonl";
            var dest = Path.Combine(logsDir, filename);
            File.Copy(logPath, dest, overwrite: true);
            File.Delete(logPath);
        }
        catch { /* Best-effort */ }
    }

    public record CliLogEntry(string Timestamp, string Command, int ExitCode, double DurationMs);
}
