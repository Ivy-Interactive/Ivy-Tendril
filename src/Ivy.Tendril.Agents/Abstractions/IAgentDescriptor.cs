namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }
    TransportKind SupportedTransports { get; }
    string? TranslateToolName(string canonicalTool);
    string? ReverseTranslateToolName(string nativeTool);
    IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools);
    IReadOnlyDictionary<string, string> GetDefaultEnvironment();
}
