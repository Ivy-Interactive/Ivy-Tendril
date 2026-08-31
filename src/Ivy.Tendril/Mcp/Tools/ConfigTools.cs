using System.ComponentModel;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using ModelContextProtocol.Server;

namespace Ivy.Tendril.Mcp.Tools;

[McpServerToolType]
public sealed class ConfigTools : AuthenticatedToolBase
{
    private readonly IConfigService _configService;
    private readonly IAgentRunner _runner;

    public ConfigTools(McpAuthenticationService authService, IConfigService configService, IAgentRunner runner) : base(authService)
    {
        _configService = configService;
        _runner = runner;
    }

    [McpServerTool(Name = "tendril_get_config"), Description("Get a top-level Tendril config value")]
    public string GetConfig(
        [Description("Config key (codingAgent, jobTimeout, staleOutputTimeout, gitTimeout, maxConcurrentJobs, planTemplate, theme)")] string key)
    {
        // Reuses the same field switch as `tendril config get` so the two surfaces never drift.
        return ExecuteAuthenticated(() => ConfigGetCommand.ReadField(_configService.Settings, key));
    }

    [McpServerTool(Name = "tendril_set_config"), Description("Set a top-level Tendril config value")]
    public string SetConfig(
        [Description("Config key (codingAgent, jobTimeout, staleOutputTimeout, gitTimeout, maxConcurrentJobs, planTemplate, theme)")] string key,
        [Description("New value. Integer fields are bounds-checked; planTemplate may be long or multiline.")] string value)
    {
        return ExecuteAuthenticated(() =>
        {
            // ApplyField throws on unknown key / bad int / out-of-range / unknown coding agent, before any write.
            _configService.MutateAndSave(s => ConfigSetCommand.ApplyField(s, key, value, _runner.RegisteredAgents));

            // Report the value actually stored (e.g. the canonical coding-agent id), not the raw input.
            var stored = ConfigGetCommand.ReadField(_configService.Settings, key);
            var summary = stored.Length <= 60 && !stored.Contains('\n') ? $"to '{stored}'" : $"({stored.Length} chars)";
            return $"Updated {key} {summary}";
        });
    }
}
