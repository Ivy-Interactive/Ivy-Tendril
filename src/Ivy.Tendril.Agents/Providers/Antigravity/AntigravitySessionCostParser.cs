using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravitySessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Antigravity;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        return new SessionCostResult
        {
            SessionId = Path.GetFileNameWithoutExtension(filePath),
            AgentId = AgentId,
        };
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".gemini", "antigravity-cli", "conversations");

        if (!Directory.Exists(dir))
            return [];

        return Directory.GetFiles(dir, "*.pb");
    }
}
