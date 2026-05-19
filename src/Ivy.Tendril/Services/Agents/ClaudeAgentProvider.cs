using System.Diagnostics;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Agents;

public class ClaudeAgentProvider : IAgentProvider
{
    public string Name => "claude";
    public bool UsesStdinPrompt => true;

    public AgentOnboardingInfo OnboardingInfo => new(
        "Claude CLI", "https://docs.anthropic.com/en/docs/claude-code", "--version",
        () => ProcessCheckHelper.CheckHealth("claude", "-p \"ping\" --max-turns 1"),
        "Sign in to Claude");

    public ProcessStartInfo BuildProcessStart(AgentInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = invocation.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--permission-mode");
        psi.ArgumentList.Add("dontAsk");

        if (!string.IsNullOrEmpty(invocation.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(invocation.Model);
        }

        if (!string.IsNullOrEmpty(invocation.Effort))
        {
            psi.ArgumentList.Add("--effort");
            psi.ArgumentList.Add(invocation.Effort);
        }

        if (!string.IsNullOrEmpty(invocation.SessionId))
        {
            psi.ArgumentList.Add("--session-id");
            psi.ArgumentList.Add(invocation.SessionId);
        }

        if (invocation.AllowedTools.Count > 0)
        {
            psi.ArgumentList.Add("--allowedTools");
            psi.ArgumentList.Add(string.Join(" ", invocation.AllowedTools));
        }

        foreach (var arg in invocation.ExtraArgs)
            psi.ArgumentList.Add(arg);

        // Prompt is read from stdin to avoid Windows command line length limits.
        psi.ArgumentList.Add("-");

        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";

        return psi;
    }

    public string? ExtractResult(IReadOnlyList<string> outputLines)
    {
        for (var i = outputLines.Count - 1; i >= 0; i--)
        {
            var line = outputLines[i];
            if (!line.Contains("\"type\":\"result\"")) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("result", out var result))
                    return result.GetString();
            }
            catch
            {
                // skip malformed JSON
            }
        }

        return null;
    }

    public IReadOnlyList<PermissionDenial> ExtractPermissionDenials(IReadOnlyList<string> outputLines)
    {
        var denials = new List<PermissionDenial>();

        for (var i = outputLines.Count - 1; i >= 0; i--)
        {
            var line = outputLines[i];
            if (!line.Contains("\"type\":\"result\"")) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("permission_denials", out var arr))
                    break;

                foreach (var denial in arr.EnumerateArray())
                {
                    var toolName = denial.TryGetProperty("tool_name", out var tn) ? tn.GetString() : null;
                    if (string.IsNullOrEmpty(toolName)) continue;

                    string? inputSummary = null;
                    if (denial.TryGetProperty("tool_input", out var input))
                    {
                        if (input.TryGetProperty("file_path", out var fp))
                            inputSummary = fp.GetString();
                        else if (input.TryGetProperty("command", out var cmd))
                        {
                            var cmdStr = cmd.GetString() ?? "";
                            inputSummary = cmdStr.Length > 80 ? cmdStr[..80] + "..." : cmdStr;
                        }
                    }

                    denials.Add(new PermissionDenial(toolName, inputSummary));
                }
            }
            catch
            {
                // skip malformed JSON
            }

            break;
        }

        return denials;
    }
}
