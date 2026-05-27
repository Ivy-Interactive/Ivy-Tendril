namespace Ivy.Tendril.Test.End2End.Helpers;

public static class AgentTestData
{
    public static readonly string[] AllAgents = ["claude", "codex", "antigravity", "copilot", "opencode"];

    public static IEnumerable<object[]> Agents
    {
        get
        {
            var filter = Environment.GetEnvironmentVariable("E2E__Agent");
            if (!string.IsNullOrEmpty(filter))
                return filter.Split(',').Select(a => new object[] { a.Trim() });
            return AllAgents.Select(a => new object[] { a });
        }
    }
}
