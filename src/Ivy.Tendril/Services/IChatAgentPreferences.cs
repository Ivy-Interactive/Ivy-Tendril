namespace Ivy.Tendril.Services;

public record ChatAgentPreference(string? ModelId = null, string? Effort = null);

public interface IChatAgentPreferences
{
    ChatAgentPreference Get(string agentId);
    void SetModel(string agentId, string modelId);
    void SetEffort(string agentId, string effort);
}
