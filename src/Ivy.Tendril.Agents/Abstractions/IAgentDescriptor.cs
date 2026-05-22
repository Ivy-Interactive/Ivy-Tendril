namespace Ivy.Tendril.Agents.Abstractions;

public enum ProfileTier
{
    Deep,
    Balanced,
    Quick
}

public sealed record AgentProfileDefault(ProfileTier Tier, string? Model, string? Effort)
{
    public string Name => Tier.ToString().ToLowerInvariant();
}

public interface IAgentDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }
    TransportKind SupportedTransports { get; }
    IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; }
    string? TranslateToolName(string canonicalTool);
    string? ReverseTranslateToolName(string nativeTool);
    IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools);
    IReadOnlyDictionary<string, string> GetDefaultEnvironment();
}
