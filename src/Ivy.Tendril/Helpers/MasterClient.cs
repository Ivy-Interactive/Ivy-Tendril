using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class MasterClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public record DiscoveryResult(string BaseUrl, string? ApiKey);
    public record JobStartResponse(string JobId, string Status);

    public static DiscoveryResult Discover(string? tendrilHome = null)
    {
        tendrilHome ??= Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrEmpty(tendrilHome))
            throw new InvalidOperationException("TENDRIL_HOME environment variable is not set.");

        var masterFilePath = Path.Combine(tendrilHome, ".master");
        if (!File.Exists(masterFilePath))
            throw new InvalidOperationException("No Tendril server is running (no .master file found). Start with 'tendril' or 'tendril run'.");

        MasterElectionService.MasterFileData data;
        try
        {
            var json = File.ReadAllText(masterFilePath);
            data = JsonSerializer.Deserialize<MasterElectionService.MasterFileData>(json, JsonOptions)!;
        }
        catch (Exception ex)
        {
            TryDelete(masterFilePath);
            throw new InvalidOperationException($"Failed to read .master file (deleted): {ex.Message}");
        }

        if (!IsProcessAlive(data.Pid))
        {
            TryDelete(masterFilePath);
            throw new InvalidOperationException($"Tendril server is not running (stale .master file, PID {data.Pid} is dead). Cleaned up.");
        }

        if (DateTime.UtcNow - data.Heartbeat > TimeSpan.FromSeconds(90))
        {
            TryDelete(masterFilePath);
            throw new InvalidOperationException("Tendril server appears hung (heartbeat stale). Cleaned up .master file.");
        }

        var apiKey = ReadApiKeyFromConfig(tendrilHome);
        return new DiscoveryResult($"http://localhost:{data.Port}", apiKey);
    }

    public static JobStartResponse SubmitJob(DiscoveryResult discovery, JobArgsBase args)
    {
        using var client = new HttpClient { Timeout = DefaultTimeout };

        if (!string.IsNullOrEmpty(discovery.ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", discovery.ApiKey);

        var json = JsonSerializer.Serialize<JobArgsBase>(args, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = client.PostAsync($"{discovery.BaseUrl}/api/jobs", content).GetAwaiter().GetResult();
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Server did not respond in time (5s timeout).");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to Tendril server: {ex.Message}");
        }

        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 401)
                throw new InvalidOperationException("Authentication failed. Check Api.ApiKey in config.yaml.");

            try
            {
                var errorDoc = JsonDocument.Parse(responseJson);
                if (errorDoc.RootElement.TryGetProperty("error", out var errorProp))
                    throw new InvalidOperationException(errorProp.GetString() ?? "Unknown server error");
            }
            catch (JsonException) { }

            throw new InvalidOperationException($"Server returned {(int)response.StatusCode}: {responseJson}");
        }

        var result = JsonSerializer.Deserialize<JobStartResponse>(responseJson, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from server");
    }

    private static string? ReadApiKeyFromConfig(string tendrilHome)
    {
        var configPath = Path.Combine(tendrilHome, "config.yaml");
        if (!File.Exists(configPath)) return null;

        try
        {
            var content = File.ReadAllText(configPath);
            return ExtractApiKey(content);
        }
        catch { return null; }
    }

    private static string? ExtractApiKey(string yamlContent)
    {
        var inApiSection = false;

        foreach (var line in yamlContent.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            var isTopLevel = trimmed.Length > 0 && trimmed[0] != ' ' && trimmed[0] != '\t';

            if (isTopLevel)
            {
                inApiSection = trimmed.StartsWith("Api:", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inApiSection) continue;

            var inner = trimmed.Trim();
            if (!inner.StartsWith("ApiKey:", StringComparison.OrdinalIgnoreCase)) continue;

            var value = inner[(inner.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(value) || value.StartsWith('%')) return null;
            return value;
        }

        return null;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
