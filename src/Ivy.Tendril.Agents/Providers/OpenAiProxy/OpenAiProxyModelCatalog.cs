using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Gemini;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyModelCatalog : IModelCatalogProvider
{
    private static readonly IvyModelCatalog IvyCatalog = new();
    private static readonly ClaudeModelCatalog ClaudeCatalog = new();
    private static readonly CodexModelCatalog CodexCatalog = new();
    private static readonly GeminiModelCatalog GeminiCatalog = new();
    private static readonly OpenCodeModelCatalog OpenCodeCatalog = new();

    private readonly Func<string?>? _baseUrlProvider;

    public OpenAiProxyModelCatalog(Func<string?>? baseUrlProvider = null)
    {
        _baseUrlProvider = baseUrlProvider;
    }

    public string AgentId => Abstractions.AgentId.OpenAiProxy;

    public IReadOnlyList<ModelInfo> GetStaticModels()
    {
        var baseUrl = _baseUrlProvider?.Invoke();
        return GetModelsForBaseUrl(baseUrl);
    }

    public Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var staticModels = GetStaticModels();
        return Task.FromResult(new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = staticModels,
            Source = ModelCatalogSource.Static,
            RetrievedAt = DateTimeOffset.UtcNow,
        });
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
