using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Copilot;
using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Agents.Providers.OpenCode;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Test;

internal static class TestAgentRunner
{
    public static IAgentRunner Create()
    {
        var runner = new AgentRunner();
        runner.Register(
            new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck(),
            new ClaudeFailureAnalyzer(), new ClaudeSessionCostParser(), new ClaudePty());
        runner.Register(
            new CodexCli(), new CodexEventParser(), new CodexHealthCheck(),
            new CodexFailureAnalyzer(), new CodexSessionCostParser(), new CodexPty());
        runner.Register(
            new AntigravityCli(), new AntigravityEventParser(), new AntigravityHealthCheck(),
            new AntigravityFailureAnalyzer(), new AntigravitySessionCostParser(), new AntigravityPty());
        runner.Register(
            new CopilotCli(), new CopilotEventParser(), new CopilotHealthCheck(),
            new CopilotFailureAnalyzer(), new CopilotSessionCostParser(), new CopilotPty());
        runner.Register(
            new OpenCodeCli(), new OpenCodeEventParser(), new OpenCodeHealthCheck(),
            new OpenCodeFailureAnalyzer(), new OpenCodeSessionCostParser(), new OpenCodePty());
        return runner;
    }
}
