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

    /// <summary>
    /// Best-effort variant of <see cref="PutJson" /> for progress telemetry: returns the failure
    /// reason instead of throwing, so a transient server-side problem (or a master restart that lost
    /// the job) can't turn a status report into a non-zero exit for a running agent.
    /// </summary>
    public static (bool Ok, string? Error) TryPutJson(string relativePath, object payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var discovery = Discover();
            using var client = CreateHttpClient(discovery);

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = client
                .PutAsync($"{discovery.BaseUrl}/{relativePath.TrimStart('/')}", content, cancellationToken)
                .GetAwaiter().GetResult();

            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"server returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (TaskCanceledException)
        {
            return (false, $"server did not respond in time ({DefaultTimeout.TotalSeconds:0}s timeout)");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"failed to connect to the Tendril server: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }

    public static (bool Ok, string? Error) TryPostJson(string relativePath, object payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var discovery = Discover();
            using var client = CreateHttpClient(discovery);

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = client
                .PostAsync($"{discovery.BaseUrl}/{relativePath.TrimStart('/')}", content, cancellationToken)
                .GetAwaiter().GetResult();

            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"server returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (TaskCanceledException)
        {
            return (false, $"server did not respond in time ({DefaultTimeout.TotalSeconds:0}s timeout)");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"failed to connect to the Tendril server: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }

    public static DiscoveryResult Discover(string? tendrilHome = null)
    {
        tendrilHome ??= Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrEmpty(tendrilHome))
            throw new InvalidOperationException("TENDRIL_HOME environment variable is not set.");

        var masterFilePath = MasterLock.GetMasterFilePath(tendrilHome);
        if (!File.Exists(masterFilePath))
            throw new InvalidOperationException("No Tendril server is running (no .master file found). Start with 'tendril' or 'tendril run'.");

        // One shared liveness test rather than a third copy of it: MasterLock decides what a live
        // claim is, and this method only turns its verdict into the CLI's wording.
        // excludeSelf: false - an embedded server discovering its own API is a legitimate case, which
        // is what the pre-MasterLock code did too.
        var data = MasterLock.ReadLiveMaster(tendrilHome, out var rejectReason, excludeSelf: false);
        if (data == null)
        {
            MasterLock.TryReclaimStale(tendrilHome);
            throw new InvalidOperationException(rejectReason switch
            {
                not null when rejectReason.StartsWith("PID ", StringComparison.Ordinal) =>
                    $"Tendril server is not running (stale .master file, {rejectReason}). Cleaned up.",
                not null when rejectReason.StartsWith("heartbeat", StringComparison.Ordinal) =>
                    "Tendril server appears hung (heartbeat stale). Cleaned up .master file.",
                _ => $"Failed to read .master file (deleted): {rejectReason}"
            });
        }

        // A claim exists but no port has been published yet: the server took the lock and has not
        // finished binding. Retrying in a moment is the right answer, not deleting its claim.
        if (data.Port == 0)
            throw new InvalidOperationException("Tendril server is still starting up, try again in a moment.");

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

}
