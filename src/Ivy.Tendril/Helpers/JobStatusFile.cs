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

    public static string GetJobLogPath(string statusFilePath) => statusFilePath + ".jsonl";

    public static void AppendCliInvocation(string statusFilePath, string command, int exitCode, double durationMs)
    {
        try
        {
            var logPath = GetJobLogPath(statusFilePath);
            var entry = new CliLogEntry(DateTime.UtcNow.ToString("O"), command, exitCode, durationMs);
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(logPath, line + "\n");
        }
        catch { /* Best-effort */ }
    }

    public static void MoveLogToPlanFolder(string statusFilePath, string planFolder, string jobType)
    {
        try
        {
            var logPath = GetJobLogPath(statusFilePath);
            if (!File.Exists(logPath)) return;

            var logsDir = Path.Combine(planFolder, "logs");
            FileHelper.EnsureDirectory(logsDir);

            var nextNumber = GetNextLogNumber(logsDir);
            var dest = Path.Combine(logsDir, $"{nextNumber:D3}-{jobType}-job.jsonl");
            File.Copy(logPath, dest, overwrite: true);
            File.Delete(logPath);
        }
        catch { /* Best-effort */ }
    }

    private static int GetNextLogNumber(string logsDir)
    {
        var max = 0;
        foreach (var file in Directory.GetFiles(logsDir))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var dashIdx = name.IndexOf('-');
            if (dashIdx > 0 && int.TryParse(name[..dashIdx], out var num) && num > max)
                max = num;
        }
        return max + 1;
    }

    public record StatusPayload(string Message, string? PlanId = null, string? PlanTitle = null);
    public record CliLogEntry(string Timestamp, string Command, int ExitCode, double DurationMs);
}
