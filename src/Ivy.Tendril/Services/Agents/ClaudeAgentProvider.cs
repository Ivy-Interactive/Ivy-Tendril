using System.Diagnostics;
using System.Text.Json;

namespace Ivy.Tendril.Services.Agents;

public class ClaudeAgentProvider : IAgentProvider
{
    public string Name => "claude";

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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--dangerously-skip-permissions");

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
            psi.ArgumentList.Add(string.Join(",", invocation.AllowedTools));
        }

        foreach (var arg in invocation.ExtraArgs)
            psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(invocation.PromptContent);

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
}
