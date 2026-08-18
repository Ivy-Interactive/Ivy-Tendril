using System.Net.Http.Headers;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Gemini;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyModelCatalog : IModelCatalogProvider
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly IvyModelCatalog IvyCatalog = new();
    private static readonly ClaudeModelCatalog ClaudeCatalog = new();
    private static readonly CodexModelCatalog CodexCatalog = new();
    private static readonly GeminiModelCatalog GeminiCatalog = new();
    private static readonly OpenCodeModelCatalog OpenCodeCatalog = new();

    private readonly Func<string?>? _baseUrlProvider;
    private readonly Func<string?>? _apiKeyProvider;

    public OpenAiProxyModelCatalog(Func<string?>? baseUrlProvider = null, Func<string?>? apiKeyProvider = null)
    {
        _baseUrlProvider = baseUrlProvider;
        _apiKeyProvider = apiKeyProvider;
    }

    public string AgentId => Abstractions.AgentId.OpenAiProxy;

    public IReadOnlyList<ModelInfo> GetStaticModels()
    {
        var baseUrl = _baseUrlProvider?.Invoke();
        return GetModelsForBaseUrl(baseUrl);
    }

    public async Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var baseUrl = _baseUrlProvider?.Invoke();
        var apiKey = _apiKeyProvider?.Invoke();
        var models = await FetchModelsFromEndpointAsync(baseUrl, apiKey, ct);
        return new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = models,
            Source = ModelCatalogSource.Dynamic,
            RetrievedAt = DateTimeOffset.UtcNow,
        };
    }

    public static async Task<IReadOnlyList<ModelInfo>> FetchModelsFromEndpointAsync(string? baseUrl, string? apiKey, CancellationToken ct = default)
    {
        var staticModels = GetModelsForBaseUrl(baseUrl);
        var url = baseUrl?.Trim().TrimEnd('/') ?? "";

        if (string.IsNullOrEmpty(url) || url.Contains("llmproxy.ivy.app") || url.Contains("ivy.app"))
        {
            return staticModels;
        }

        try
        {
            var fetched = await TryFetchModelsHttpAsync(url, apiKey, ct);
            if (fetched is { Count: > 0 })
            {
                var allKnownLookup = new IModelCatalogProvider[] { CodexCatalog, ClaudeCatalog, GeminiCatalog, IvyCatalog, OpenCodeCatalog }
                    .SelectMany(c => c.GetStaticModels())
                    .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

                var result = new List<ModelInfo>();
                foreach (var (id, name) in fetched)
                {
                    if (allKnownLookup.TryGetValue(id, out var known))
                    {
                        result.Add(known);
                    }
                    else
                    {
                        result.Add(new ModelInfo
                        {
                            Id = id,
                            DisplayName = !string.IsNullOrEmpty(name) && name != id ? name : id,
                            Capabilities = ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming,
                            SupportedEfforts = EffortLevels.Codex,
                            Provider = "custom",
                        });
                    }
                }

                // Add any remaining static models from the provider that were not in the fetched list
                foreach (var sm in staticModels)
                {
                    if (!result.Any(r => r.Id.Equals(sm.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(sm);
                    }
                }

                return result
                    .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch
        {
            // Fallback to static models on error
        }

        return staticModels;
    }

    private static async Task<List<(string Id, string? Name)>?> TryFetchModelsHttpAsync(string baseUrl, string? apiKey, CancellationToken ct)
    {
        var isAnthropic = baseUrl.Contains("api.anthropic.com");
        var urlsToTry = new List<string>();

        if (isAnthropic)
        {
            urlsToTry.Add("https://api.anthropic.com/v1/models");
        }
        else
        {
            if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                urlsToTry.Add($"{baseUrl}/models");
            }
            else
            {
                urlsToTry.Add($"{baseUrl}/v1/models");
                urlsToTry.Add($"{baseUrl}/models");
            }
        }

        foreach (var endpointUrl in urlsToTry)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    if (isAnthropic)
                    {
                        request.Headers.Add("x-api-key", apiKey);
                        request.Headers.Add("anthropic-version", "2023-06-01");
                    }
                    else
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    }
                }

                using var response = await HttpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    var models = ParseModelsJson(json);
                    if (models.Count > 0)
                    {
                        return models;
                    }
                }
            }
            catch
            {
                // Continue to next URL attempt
            }
        }

        return null;
    }

    private static List<(string Id, string? Name)> ParseModelsJson(string json)
    {
        var list = new List<(string Id, string? Name)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // OpenAI / Anthropic format: { "data": [ { "id": "...", "display_name": "..." } ] }
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataEl.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl) && idEl.GetString() is { } id && !string.IsNullOrWhiteSpace(id))
                    {
                        string? name = null;
                        if (item.TryGetProperty("display_name", out var dnEl)) name = dnEl.GetString();
                        else if (item.TryGetProperty("name", out var nEl)) name = nEl.GetString();
                        list.Add((id, name));
                    }
                }
            }
            // Ollama format: { "models": [ { "name": "...", "model": "..." } ] }
            else if (root.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsEl.EnumerateArray())
                {
                    string? id = null;
                    if (item.TryGetProperty("name", out var nEl)) id = nEl.GetString();
                    else if (item.TryGetProperty("model", out var mEl)) id = mEl.GetString();
                    else if (item.TryGetProperty("id", out var idEl)) id = idEl.GetString();

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        list.Add((id, id));
                    }
                }
            }
            // Direct array: [ { "id": "..." } ]
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl) && idEl.GetString() is { } id && !string.IsNullOrWhiteSpace(id))
                    {
                        list.Add((id, id));
                    }
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return list;
    }

    public static IReadOnlyList<ModelInfo> GetModelsForBaseUrl(string? baseUrl)
    {
        var url = baseUrl ?? "";
        if (url.Contains("llmproxy.ivy.app") || url.Contains("ivy.app"))
        {
            return IvyCatalog.GetStaticModels();
        }

        if (url.Contains("api.berget.ai"))
        {
            return
            [
                .. OpenCodeCatalog.GetStaticModels().Where(m => m.Id != "default"),
                new ModelInfo
                {
                    Id = "Qwen/Qwen2.5-Coder-32B-Instruct",
                    DisplayName = "Qwen 2.5 Coder 32B",
                    Capabilities = ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming,
                    SupportedEfforts = EffortLevels.OpenCode,
                    Provider = "berget",
                    ContextWindow = 128000,
                }
            ];
        }

        if (url.Contains("api.anthropic.com"))
        {
            return ClaudeCatalog.GetStaticModels();
        }

        if (url.Contains("generativelanguage.googleapis.com") || url.Contains("gemini") || url.Contains("google"))
        {
            return GeminiCatalog.GetStaticModels();
        }

        if (url.Contains("api.openai.com") || string.IsNullOrEmpty(url))
        {
            return CodexCatalog.GetStaticModels();
        }

        // Custom URL: return unified list of OpenAI, Claude (including 4.6, 4.7, 4.8, 5), Google, OpenCode, and Ivy models
        var combined = new List<ModelInfo>();
        combined.AddRange(CodexCatalog.GetStaticModels());
        combined.AddRange(ClaudeCatalog.GetStaticModels());
        combined.AddRange(GeminiCatalog.GetStaticModels());
        combined.AddRange(OpenCodeCatalog.GetStaticModels().Where(m => m.Id != "default"));
        combined.AddRange(IvyCatalog.GetStaticModels());

        return combined
            .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
