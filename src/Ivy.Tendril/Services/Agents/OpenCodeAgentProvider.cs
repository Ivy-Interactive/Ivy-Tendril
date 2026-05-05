using System.Diagnostics;

namespace Ivy.Tendril.Services.Agents;

public class OpenCodeAgentProvider : IAgentProvider
{
    public string Name => "opencode";
    public bool UsesStdinPrompt => true;

    public ProcessStartInfo BuildProcessStart(AgentInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "opencode",
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

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--dangerously-skip-permissions");
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("json");

        if (!string.IsNullOrEmpty(invocation.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(invocation.Model);
        }

        if (!string.IsNullOrEmpty(invocation.Effort))
        {
            psi.ArgumentList.Add("--variant");
            psi.ArgumentList.Add(invocation.Effort);
        }

        if (!string.IsNullOrEmpty(invocation.SessionId))
        {
            psi.ArgumentList.Add("--session");
            psi.ArgumentList.Add(invocation.SessionId);
        }

        foreach (var arg in invocation.ExtraArgs)
            psi.ArgumentList.Add(arg);

        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";

        return psi;
    }

    public string? ExtractResult(IReadOnlyList<string> outputLines)
    {
        for (var i = outputLines.Count - 1; i >= 0; i--)
        {
            var line = outputLines[i].Trim();
            if (!string.IsNullOrEmpty(line))
                return line;
        }

        return null;
    }
}
