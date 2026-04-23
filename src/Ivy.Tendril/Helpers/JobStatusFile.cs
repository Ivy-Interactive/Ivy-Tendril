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
        return Path.Combine(tendrilHome, "jobs", $"{jobId}.status");
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

    public record StatusPayload(string Message, string? PlanId = null, string? PlanTitle = null);
}
