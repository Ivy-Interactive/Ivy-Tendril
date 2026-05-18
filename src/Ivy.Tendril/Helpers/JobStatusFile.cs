using System.Text.Json;

namespace Ivy.Tendril.Helpers;

public static class JobStatusFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetStatusFilePath(string jobId)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        return Path.Combine(tendrilHome, "Jobs", $"{jobId}.status");
    }

    public static void Write(string statusFilePath, string message, string? planId = null, string? planTitle = null)
    {
        var dir = Path.GetDirectoryName(statusFilePath)!;
        FileHelper.EnsureDirectory(dir);

        var payload = new StatusPayload(message, planId, planTitle);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        FileHelper.WriteAllText(statusFilePath, json);
    }

    public static StatusPayload? Read(string statusFilePath)
    {
        try
        {
            if (!File.Exists(statusFilePath)) return null;
            var json = File.ReadAllText(statusFilePath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<StatusPayload>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string statusFilePath)
    {
        try { if (File.Exists(statusFilePath)) File.Delete(statusFilePath); }
        catch { /* Best-effort */ }
    }

    private static string GetJobLogPath(string statusFilePath) => statusFilePath + ".jsonl";

    // public static void AppendCliInvocation(string statusFilePath, string command, int exitCode, double durationMs)
    // {
    //     try
    //     {
    //         var logPath = GetJobLogPath(statusFilePath);
    //         var entry = new CliLogEntry(DateTime.UtcNow.ToString("O"), command, exitCode, durationMs);
    //         var line = JsonSerializer.Serialize(entry, JsonOptions);
    //         File.AppendAllText(logPath, line + "\n");
    //     }
    //     catch { /* Best-effort */ }
    // }

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

    public static void MoveLogToPlanFolder(string statusFilePath, string planFolder, string jobType, string? jobId = null)
    {
        try
        {
            var logPath = GetJobLogPath(statusFilePath);
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

    public record StatusPayload(string Message, string? PlanId = null, string? PlanTitle = null);
    public record CliLogEntry(string Timestamp, string Command, int ExitCode, double DurationMs);
}
