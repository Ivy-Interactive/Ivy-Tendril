using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Copilot;
using Ivy.Tendril.Agents.Providers.OpenCode;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.End2End.Fixtures;

public sealed class AgentFixture : IAsyncLifetime
{
    public AgentRunner Runner { get; private set; } = null!;
    public string WorkingDirectory { get; private set; } = null!;

    private readonly Dictionary<string, AgentInstallStatus> _installCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AgentAuthResult> _authCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        Runner = new AgentRunner();
        Runner.Register(new AntigravityCli(), new AntigravityEventParser(), new AntigravityHealthCheck(), new AntigravityFailureAnalyzer(), new AntigravitySessionCostParser(), new AntigravityPty());
        Runner.Register(new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck(), new ClaudeFailureAnalyzer(), new ClaudeSessionCostParser(), new ClaudePty());
        Runner.Register(new CodexCli(), new CodexEventParser(), new CodexHealthCheck(), new CodexFailureAnalyzer(), new CodexSessionCostParser(), new CodexPty());
        Runner.Register(new CopilotCli(), new CopilotEventParser(), new CopilotHealthCheck(), new CopilotFailureAnalyzer(), new CopilotSessionCostParser(), new CopilotPty());
        Runner.Register(new OpenCodeCli(), new OpenCodeEventParser(), new OpenCodeHealthCheck(), new OpenCodeFailureAnalyzer(), new OpenCodeSessionCostParser(), new OpenCodePty());

        WorkingDirectory = Path.Combine(Path.GetTempPath(), "ivy-agents-e2e", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(WorkingDirectory);

        foreach (var agentId in Runner.RegisteredAgents)
        {
            var hc = Runner.GetHealthCheck(agentId);
            try
            {
                _installCache[agentId] = await hc.CheckInstallAsync();
                if (_installCache[agentId].IsInstalled)
                    _authCache[agentId] = await hc.CheckAuthAsync();
            }
            catch (Exception ex)
            {
                _installCache[agentId] = new AgentInstallStatus
                {
                    IsInstalled = false,
                    Error = $"Health check threw: {ex.Message}",
                };
            }
        }
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(WorkingDirectory))
        {
            try { Directory.Delete(WorkingDirectory, recursive: true); }
            catch { /* best effort */ }
        }
        return Task.CompletedTask;
    }

    public bool IsAvailable(string agentId)
    {
        return _installCache.TryGetValue(agentId, out var install)
               && install.IsInstalled
               && _authCache.TryGetValue(agentId, out var auth)
               && auth.Status == AuthStatus.Authenticated;
    }

    public string SkipReasonIfUnavailable(string agentId)
    {
        if (!_installCache.TryGetValue(agentId, out var install) || !install.IsInstalled)
            return $"{agentId} CLI is not installed";
        if (!_authCache.TryGetValue(agentId, out var auth) || auth.Status != AuthStatus.Authenticated)
            return $"{agentId} is not authenticated (status: {auth?.Status})";
        return string.Empty;
    }
}

[CollectionDefinition("Agents")]
public class AgentCollection : ICollectionFixture<AgentFixture>;
