using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class AgentModelsCheck : IDoctorCheck
{
    private static readonly Dictionary<string, string> VersionArgs = new()
    {
        ["gh"] = "--version",
        ["claude"] = "--version",
        ["codex"] = "--version",
        ["gemini"] = "--version",
        ["git"] = "--version",
        ["pwsh"] = "-Version",
        ["pandoc"] = "--version"
    };

    private readonly ConfigService? _configService;

    public AgentModelsCheck(ConfigService? configService = null)
    {
        _configService = configService;
    }

    public string Name => "Agent Models";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        if (_configService?.Settings.CodingAgents is not { Count: > 0 } agents)
        {
            return new CheckResult(false, statuses);
        }

        var codingAgent = _configService?.Settings.CodingAgent ?? "claude";
        var activeAgent = agents.FirstOrDefault(a =>
            string.Equals(a.Name, codingAgent, StringComparison.OrdinalIgnoreCase) ||
            (a.Name == "ClaudeCode" && codingAgent == "claude") ||
            (a.Name == "Codex" && codingAgent == "codex") ||
            (a.Name == "Gemini" && codingAgent == "gemini"));

        foreach (var agent in agents)
        {
            var isActive = agent == activeAgent;
            var label = isActive ? $"{agent.Name} (active)" : agent.Name;

            if (agent.Profiles.Count == 0)
            {
                statuses.Add(new CheckStatus(label, "No profiles configured", StatusKind.Warn));
                continue;
            }

            var cliName = ResolveCliName(agent.Name, codingAgent);
            var cliInstalled = cliName != null && await ProcessHelper.CheckCommand(cliName, VersionArgs.GetValueOrDefault(cliName, "--version"));

            if (!cliInstalled)
            {
                statuses.Add(new CheckStatus(label, $"CLI '{cliName ?? agent.Name}' not found — skipping", StatusKind.Warn));
                continue;
            }

            // Add parent agent status
            statuses.Add(new CheckStatus($"  {label}", "", StatusKind.Ok));

            foreach (var profile in agent.Profiles)
            {
                if (string.IsNullOrEmpty(profile.Model))
                {
                    statuses.Add(new CheckStatus($"    {profile.Name}", "No model specified", StatusKind.Warn));
                    continue;
                }

                var modelResult = await VerifyModel(cliName!, agent.Name, profile.Model);
                var profileLabel = $"    {profile.Name}: {profile.Model}";
                switch (modelResult)
                {
                    case ModelResult.Ok:
                        statuses.Add(new CheckStatus(profileLabel, "OK", StatusKind.Ok));
                        break;
                    case ModelResult.InvalidModel:
                        statuses.Add(new CheckStatus(profileLabel, "Invalid model ID", StatusKind.Error));
                        hasErrors = true;
                        break;
                    case ModelResult.AuthError:
                        statuses.Add(new CheckStatus(profileLabel, "Auth error", StatusKind.Error));
                        hasErrors = true;
                        break;
                    case ModelResult.Timeout:
                        statuses.Add(new CheckStatus(profileLabel, "Timeout (30s)", StatusKind.Warn));
                        break;
                    case ModelResult.Unknown:
                        statuses.Add(new CheckStatus(profileLabel, "Check failed", StatusKind.Warn));
                        break;
                }
            }
        }

        return new CheckResult(hasErrors, statuses);
    }

    private static string? ResolveCliName(string agentName, string codingAgent)
    {
        return agentName.ToLower() switch
        {
            "claude" or "claudecode" => "claude",
            "codex" => "codex",
            "gemini" => "gemini",
            _ => null
        };
    }

    private static async Task<ModelResult> VerifyModel(string cli, string agentName, string model)
    {
        var (args, timeout) = cli switch
        {
            "claude" => ($"-p \"ping\" --model {model} --max-turns 1", 30000),
            "gemini" => ($"-p \"Reply OK\" --model {model}", 30000),
            "codex" => ($"exec --model {model} \"Reply OK\"", 60000),
            _ => ("", 0)
        };

        if (string.IsNullOrEmpty(args)) return ModelResult.Unknown;

        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(ProcessHelper.MakeStartInfo(cli, args));
                if (proc is null) return ModelResult.Unknown;

                var stderr = "";
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) stderr += e.Data + "\n";
                };
                proc.BeginErrorReadLine();
                proc.StandardOutput.ReadToEnd();

                var exited = proc.WaitForExitOrKill(timeout);
                if (!exited) return ModelResult.Timeout;
                if (proc.ExitCode == 0) return ModelResult.Ok;

                if (IsInvalidModelError(stderr))
                    return ModelResult.InvalidModel;

                if (IsAuthError(stderr))
                    return ModelResult.AuthError;

                return ModelResult.InvalidModel;
            });
        }
        catch
        {
            return ModelResult.Unknown;
        }
    }

    private static bool IsInvalidModelError(string stderr) =>
        stderr.Contains("model identifier is invalid", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthError(string stderr) =>
        stderr.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("401", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("403", StringComparison.OrdinalIgnoreCase);
}

internal enum ModelResult { Ok, InvalidModel, AuthError, Timeout, Unknown }
