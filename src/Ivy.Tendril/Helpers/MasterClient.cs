using System.Diagnostics;
using System.Net.Http;
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

    public static HttpClient CreateHttpClient(DiscoveryResult discovery)
    {
        HttpClient client;
        if (discovery.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            client = new HttpClient(handler) { Timeout = DefaultTimeout };
        }
        else
        {
            client = new HttpClient { Timeout = DefaultTimeout };
        }

        if (!string.IsNullOrEmpty(discovery.ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", discovery.ApiKey);

        return client;
    }

    /// <summary>
    /// Discovers the running Tendril server and issues a JSON PUT to the given relative path
    /// (e.g. "api/jobs/00001/status"), throwing on a non-success status. Shared by the CLI
    /// commands that report job state so the discover/serialize/PUT convention lives in one place.
    /// </summary>
    /// <param name="notFoundMessage">
    /// Overrides the default 404 message with caller-supplied text (e.g. naming the job id).
    /// </param>
    public static void PutJson(string relativePath, object payload, string? notFoundMessage = null, CancellationToken cancellationToken = default)
    {
        var discovery = Discover();
        using var client = CreateHttpClient(discovery);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = client.PutAsync($"{discovery.BaseUrl}/{relativePath.TrimStart('/')}", content, cancellationToken)
                .GetAwaiter().GetResult();
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException($"Server did not respond in time (5s timeout) for {relativePath}.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to Tendril server for {relativePath}: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

            if ((int)response.StatusCode == 404 && notFoundMessage != null)
                throw new InvalidOperationException(notFoundMessage);

            throw new InvalidOperationException(DescribeFailure((int)response.StatusCode, relativePath, responseBody));
        }
    }

    /// <summary>
    /// Turns a failed HTTP response into a message naming the endpoint and, when the body
    /// carries an <c>{"error": ...}</c> property, the server's own explanation. Shared by
    /// <see cref="PutJson"/> and <see cref="SubmitJob"/> so both agree on wording.
    /// </summary>
    internal static string DescribeFailure(int statusCode, string relativePath, string responseBody)
    {
        if (statusCode == 401)
            return "Authentication failed. Check Api.ApiKey in config.yaml.";

        if (statusCode == 404)
            return $"Server does not know '{relativePath}' (404). The Tendril server may have restarted since the job started, or the job was deleted.";

        try
        {
            var errorDoc = JsonDocument.Parse(responseBody);
            if (errorDoc.RootElement.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? "Unknown server error";
        }
        catch (JsonException) { }

        return $"Server returned {statusCode} for {relativePath}: {responseBody}";
    }

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

        var scheme = string.IsNullOrEmpty(data.Scheme) ? "http" : data.Scheme;
        var apiKey = ReadApiKeyFromConfig(tendrilHome);
        return new DiscoveryResult($"{scheme}://localhost:{data.Port}", apiKey);
    }

    public static JobStartResponse SubmitJob(DiscoveryResult discovery, JobArgsBase args)
    {
        using var client = CreateHttpClient(discovery);

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
            throw new InvalidOperationException(DescribeFailure((int)response.StatusCode, "api/jobs", responseJson));

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
