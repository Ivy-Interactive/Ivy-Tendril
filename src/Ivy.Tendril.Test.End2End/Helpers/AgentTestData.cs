namespace Ivy.Tendril.Test.End2End.Helpers;

public static class AgentTestData
{
    public static readonly string[] AllAgents = ["claude", "codex", "gemini", "copilot", "opencode"];

    public static IEnumerable<object[]> Agents =>
        AllAgents.Select(a => new object[] { a });
}
