using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyModelCatalog : IModelCatalogProvider
{
    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming;

    private readonly OpenCodeModelCatalog _inner = new();
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

    public async Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var staticModels = GetStaticModels();
        return new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = staticModels,
            Source = ModelCatalogSource.Static,
            RetrievedAt = DateTimeOffset.UtcNow,
        };
    }

    public static IReadOnlyList<ModelInfo> GetModelsForBaseUrl(string? baseUrl)
    {
        var url = baseUrl ?? "";
        if (url.Contains("llmproxy.ivy.app") || url.Contains("ivy.app"))
        {
            return
            [
                new ModelInfo { Id = "ivy-stem", DisplayName = "Ivy Stem", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Ivy, Provider = "ivy", IsDefault = true },
                new ModelInfo { Id = "ivy-root", DisplayName = "Ivy Root", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Ivy, Provider = "ivy" },
                new ModelInfo { Id = "ivy-leaf", DisplayName = "Ivy Leaf", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Ivy, Provider = "ivy" },
            ];
        }

        if (url.Contains("api.berget.ai"))
        {
            return
            [
                new ModelInfo { Id = "moonshotai/Kimi-K3", DisplayName = "Kimi K3", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "berget", IsDefault = true, ContextWindow = 200000 },
                new ModelInfo { Id = "kimi-k2", DisplayName = "Kimi K2", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "berget", ContextWindow = 200000 },
                new ModelInfo { Id = "deepseek-ai/DeepSeek-V3", DisplayName = "DeepSeek V3", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "berget", ContextWindow = 128000 },
                new ModelInfo { Id = "deepseek-ai/DeepSeek-R1", DisplayName = "DeepSeek R1", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "berget", ContextWindow = 128000 },
                new ModelInfo { Id = "Qwen/Qwen2.5-Coder-32B-Instruct", DisplayName = "Qwen 2.5 Coder 32B", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "berget", ContextWindow = 128000 },
            ];
        }

        if (url.Contains("api.anthropic.com"))
        {
            return
            [
                new ModelInfo { Id = "claude-opus-5", DisplayName = "Claude Opus 5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic", IsDefault = true },
                new ModelInfo { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-haiku-5", DisplayName = "Claude Haiku 5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-opus-4-7", DisplayName = "Claude Opus 4.7", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-3-7-sonnet", DisplayName = "Claude 3.7 Sonnet", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-3-5-sonnet", DisplayName = "Claude 3.5 Sonnet", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
                new ModelInfo { Id = "claude-3-5-haiku", DisplayName = "Claude 3.5 Haiku", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
            ];
        }

        if (url.Contains("api.openai.com") || string.IsNullOrEmpty(url))
        {
            return
            [
                new ModelInfo { Id = "gpt-5.6-sol", DisplayName = "GPT-5.6-Sol", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", IsDefault = true, ContextWindow = 400000 },
                new ModelInfo { Id = "gpt-5.6-terra", DisplayName = "GPT-5.6-Terra", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 272000 },
                new ModelInfo { Id = "gpt-5.6-luna", DisplayName = "GPT-5.6-Luna", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 272000 },
                new ModelInfo { Id = "gpt-5.5", DisplayName = "GPT-5.5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 400000 },
                new ModelInfo { Id = "gpt-5.4", DisplayName = "GPT-5.4", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 400000 },
                new ModelInfo { Id = "gpt-5.4-mini", DisplayName = "GPT-5.4 Mini", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 400000 },
                new ModelInfo { Id = "gpt-4o", DisplayName = "GPT-4o", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 128000 },
                new ModelInfo { Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 128000 },
                new ModelInfo { Id = "o3", DisplayName = "O3", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 200000 },
                new ModelInfo { Id = "o3-mini", DisplayName = "O3 Mini", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 200000 },
                new ModelInfo { Id = "o1", DisplayName = "O1", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 200000 },
            ];
        }

        // Custom URL: return unified list
        return
        [
            new ModelInfo { Id = "gpt-5.6-sol", DisplayName = "GPT-5.6-Sol", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", IsDefault = true, ContextWindow = 400000 },
            new ModelInfo { Id = "gpt-5.6-terra", DisplayName = "GPT-5.6-Terra", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 272000 },
            new ModelInfo { Id = "gpt-5.6-luna", DisplayName = "GPT-5.6-Luna", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Codex, Provider = "openai", ContextWindow = 272000 },
            new ModelInfo { Id = "claude-opus-5", DisplayName = "Claude Opus 5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
            new ModelInfo { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
            new ModelInfo { Id = "claude-haiku-5", DisplayName = "Claude Haiku 5", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
            new ModelInfo { Id = "claude-3-7-sonnet", DisplayName = "Claude 3.7 Sonnet", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
            new ModelInfo { Id = "claude-3-5-sonnet", DisplayName = "Claude 3.5 Sonnet", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic" },
            new ModelInfo { Id = "moonshotai/Kimi-K3", DisplayName = "Kimi K3", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "moonshot" },
            new ModelInfo { Id = "deepseek-ai/DeepSeek-V3", DisplayName = "DeepSeek V3", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "deepseek" },
            new ModelInfo { Id = "deepseek-ai/DeepSeek-R1", DisplayName = "DeepSeek R1", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "deepseek" },
            new ModelInfo { Id = "ivy-stem", DisplayName = "Ivy Stem", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Ivy, Provider = "ivy" },
            new ModelInfo { Id = "ivy-root", DisplayName = "Ivy Root", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Ivy, Provider = "ivy" },
            new ModelInfo { Id = "ivy-leaf", DisplayName = "Ivy Leaf", Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Ivy, Provider = "ivy" },
        ];
    }
}
