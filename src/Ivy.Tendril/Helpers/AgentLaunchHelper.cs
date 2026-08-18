using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class AgentLaunchHelper
{
    public static string GetDefaultWorkDir(IConfigService config) =>
        !string.IsNullOrEmpty(config.TendrilHome)
            ? config.TendrilHome
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string GetWorkDir(IConfigService config, IAgentRunner runner, string? agentId = null)
    {
        var targetAgentId = agentId ?? config.Settings.CodingAgent;
        var pty = runner.GetPty(targetAgentId);
        var defaultDir = GetDefaultWorkDir(config);
        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = defaultDir,
            PermissionMode = PermissionMode.Default,
        });
        return spec?.WorkingDirectory ?? defaultDir;
    }

    public static string? CompileSystemPrompt(IConfigService config)
    {
        return AgentPromptCompiler.Compile(config);
    }

    public static void WriteAgentInstructionsIfNeeded(string workDir, string? systemPrompt, IAgentPty? pty)
    {
        if (string.IsNullOrEmpty(systemPrompt) || string.IsNullOrEmpty(workDir))
            return;

        // Each agent declares the file it reads for project/system instructions (AGENTS.md,
        // GEMINI.md, …). When ContextFileName is null the agent takes its system prompt via a
        // command-line flag instead (Claude → --append-system-prompt-file) and needs no file.
        var contextFile = pty?.ContextFileName;
        if (string.IsNullOrEmpty(contextFile))
            return;

        File.WriteAllText(Path.Combine(workDir, contextFile), systemPrompt);
    }

    public static void WriteAgentInstructionsIfNeeded(string workDir, string? systemPrompt, IAgentRunner runner, string agentId)
    {
        var pty = runner.GetPty(agentId);
        WriteAgentInstructionsIfNeeded(workDir, systemPrompt, pty);
    }

    public static Dictionary<string, string> GetEnvironment(IConfigService config, string? agentId = null)
    {
        var env = new Dictionary<string, string>();

        var targetAgentId = agentId ?? config.Settings.CodingAgent;
        var agentConfig = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(targetAgentId, StringComparison.OrdinalIgnoreCase));
        if (agentConfig?.EnvironmentVariables is { Count: > 0 } d)
        {
            foreach (var (key, value) in d)
                env[key] = value;
        }

        // Expose the `tendril` CLI (via shim) and the active config/plans to the agent running in
        // the process/PTY, so it can run `tendril ...` even when no tendril binary is installed (e.g. in dev).
        AgentProcessHelper.ApplyTendrilEnvironment(env, config);

        return env;
    }

    public static string? ResolveModel(IConfigService config, IAgentRunner runner, string agentId, string? requestedModel = null)
    {
        if (!string.IsNullOrEmpty(requestedModel) && requestedModel != "default")
        {
            return requestedModel;
        }

        var pty = runner.GetPty(agentId);
        if (pty?.Id == AgentId.Claude)
        {
            return "default";
        }

        var agentConfig = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var configuredModel = agentConfig?.Profiles.FirstOrDefault(p => p.Name.Equals("balanced", StringComparison.OrdinalIgnoreCase))?.Model;
        if (string.IsNullOrEmpty(configuredModel) || configuredModel == "default")
        {
            configuredModel = agentConfig?.Profiles.FirstOrDefault(p => !string.IsNullOrEmpty(p.Model) && p.Model != "default")?.Model;
        }

        var isBergetAgent = agentId.Equals("berget", StringComparison.OrdinalIgnoreCase) ||
            (agentConfig != null && agentConfig.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url) && url.Contains("api.berget.ai"));

        if (isBergetAgent)
        {
            if (string.IsNullOrEmpty(configuredModel) || configuredModel == "default" || configuredModel.Equals("kimi-k3", StringComparison.OrdinalIgnoreCase) || !configuredModel.Contains('/'))
            {
                configuredModel = "moonshotai/Kimi-K3";
            }
        }

        return string.IsNullOrEmpty(configuredModel) ? (requestedModel ?? "default") : configuredModel;
    }

    public static EffortLevel? ResolveEffort(IConfigService config, IAgentRunner runner, string agentId, string? profileName = "balanced")
    {
        var agentConfig = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var configuredEffort = agentConfig?.Profiles.FirstOrDefault(p => p.Name.Equals(profileName ?? "balanced", StringComparison.OrdinalIgnoreCase))?.Effort;
        if (string.IsNullOrEmpty(configuredEffort) || configuredEffort == "default")
        {
            configuredEffort = agentConfig?.Profiles.FirstOrDefault(p => !string.IsNullOrEmpty(p.Effort) && p.Effort != "default")?.Effort;
        }

        return string.IsNullOrEmpty(configuredEffort) ? null : AgentProviderFactory.ParseEffort(configuredEffort);
    }

    public static AgentResolutionContext PrepareResolutionContext(
        IConfigService config,
        IAgentRunner runner,
        string agentId,
        string prompt,
        string? modelOverride = null,
        PermissionMode permissionMode = PermissionMode.FullAuto)
    {
        var workDir = GetWorkDir(config, runner, agentId);
        var systemPrompt = CompileSystemPrompt(config);
        WriteAgentInstructionsIfNeeded(workDir, systemPrompt, runner, agentId);

        var resolvedModel = ResolveModel(config, runner, agentId, modelOverride);
        var resolvedEffort = ResolveEffort(config, runner, agentId);
        var env = GetEnvironment(config, agentId);

        return new AgentResolutionContext
        {
            AgentId = agentId,
            Prompt = prompt,
            SystemPrompt = systemPrompt,
            ModelOverride = resolvedModel,
            EffortOverride = resolvedEffort,
            WorkingDirectory = workDir,
            PermissionMode = permissionMode,
            ExtraEnvironment = env,
        };
    }

    public static AgentActivityPatterns? GetActivityPatterns(IConfigService config, IAgentRunner runner, string? agentId = null)
    {
        var targetAgentId = agentId ?? config.Settings.CodingAgent;
        var pty = runner.GetPty(targetAgentId);
        return pty?.GetActivityPatterns();
    }
}
