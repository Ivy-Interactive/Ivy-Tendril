using System.Net.Http.Headers;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Gemini;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public record FetchModelsResult
{
    public bool Success { get; init; }
    public IReadOnlyList<ModelInfo> Models { get; init; } = Array.Empty<ModelInfo>();
    public bool IsAuthError { get; init; }
    public string? ErrorMessage { get; init; }
}

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
        if (models.Count == 0)
        {
            models = GetStaticModels();
        }
        else
        {
            models = ModelCatalogSorter.Sort(models);
        }
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
        var result = await FetchModelsDetailedAsync(baseUrl, apiKey, ct);
        return result.Models;
    }

    public static async Task<FetchModelsResult> FetchModelsDetailedAsync(string? baseUrl, string? apiKey, CancellationToken ct = default)
    {
        var url = baseUrl?.Trim().TrimEnd('/') ?? "";

        if (string.IsNullOrEmpty(url))
        {
            return new FetchModelsResult { Success = false, ErrorMessage = "Base URL is not configured." };
        }

        try
        {
            var isAnthropic = url.Contains("api.anthropic.com");
            var urlsToTry = new List<string>();

            if (isAnthropic)
            {
                urlsToTry.Add("https://api.anthropic.com/v1/models");
            }
            else
            {
                if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    urlsToTry.Add($"{url}/models");
                }
                else
                {
                    urlsToTry.Add($"{url}/v1/models");
                    urlsToTry.Add($"{url}/models");
                }
            }

            string? lastError = null;
            bool isAuthError = false;

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
                            if (url.Contains("ivy.app") || url.Contains("llmproxy"))
                            {
                                request.Headers.Add("x-api-key", apiKey);
                            }
                        }
                    }

                    using var response = await HttpClient.SendAsync(request, ct);
                    var content = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        var fetched = ParseModelsJson(content);
                        if (fetched.Count > 0)
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

                            return new FetchModelsResult
                            {
                                Success = true,
                                Models = ModelCatalogSorter.Sort(result.DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList())
                            };
                        }
                    }

                    var errMsg = LlmEndpointTester.ExtractErrorMessage(content, (int)response.StatusCode);
                    lastError = errMsg;
                    if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ||
                        errMsg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
                        errMsg.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                        errMsg.Contains("authentication_error", StringComparison.OrdinalIgnoreCase) ||
                        errMsg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                    {
                        isAuthError = true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            return new FetchModelsResult
            {
                Success = false,
                IsAuthError = isAuthError,
                ErrorMessage = lastError
            };
        }
        catch (Exception ex)
        {
            return new FetchModelsResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
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

        return ModelCatalogSorter.Sort(combined
            .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    public static async Task<(bool Success, string? ErrorMessage)> TestModelEndpointAsync(
        string? baseUrl,
        string? apiKey,
        string model,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model) || model == "__custom__" || model == "default")
        {
            return (false, "Please specify a valid model name.");
        }

        var res = await LlmEndpointTester.TestModelPromptAsync(baseUrl ?? "", apiKey ?? "", model, ct);
        return (res.Status == ModelValidationStatus.Ok, res.ErrorMessage);
    }
}
