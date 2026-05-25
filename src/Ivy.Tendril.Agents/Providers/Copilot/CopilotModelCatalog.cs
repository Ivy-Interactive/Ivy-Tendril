using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Copilot;

public sealed class CopilotModelCatalog : CachedModelCatalogProvider
{
    public override string AgentId => Abstractions.AgentId.Copilot;

    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new() { Id = "gpt-5.4", DisplayName = "GPT-5.4", Capabilities = DefaultCaps, Provider = "openai", IsDefault = true },
        new() { Id = "gpt-5.3-codex", DisplayName = "GPT-5.3 Codex", Capabilities = DefaultCaps, Provider = "openai" },
        new() { Id = "gpt-5.2-codex", DisplayName = "GPT-5.2 Codex", Capabilities = DefaultCaps, Provider = "openai" },
        new() { Id = "gpt-5.2", DisplayName = "GPT-5.2", Capabilities = DefaultCaps, Provider = "openai" },
        new() { Id = "gpt-5.4-mini", DisplayName = "GPT-5.4 Mini", Capabilities = DefaultCaps, Provider = "openai" },
        new() { Id = "gpt-5-mini", DisplayName = "GPT-5 Mini", Capabilities = DefaultCaps, Provider = "openai" },
        new() { Id = "gpt-4.1", DisplayName = "GPT-4.1", Capabilities = DefaultCaps, Provider = "openai" },
        new() { Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6", Capabilities = DefaultCaps, Provider = "anthropic" },
        new() { Id = "claude-sonnet-4-5", DisplayName = "Claude Sonnet 4.5", Capabilities = DefaultCaps, Provider = "anthropic" },
        new() { Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", Capabilities = DefaultCaps, Provider = "anthropic" },
    ];
}
