using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services.Agents;

public class CodexAgentProvider : IAgentProvider
{
    public string Name => "codex";

    public ProcessStartInfo BuildProcessStart(AgentInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "codex",
            WorkingDirectory = invocation.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        psi.ArgumentList.Add("--full-auto");

        if (!string.IsNullOrEmpty(invocation.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(invocation.Model);
        }

        if (!string.IsNullOrEmpty(invocation.Effort))
        {
            psi.ArgumentList.Add("--reasoning-effort");
            psi.ArgumentList.Add(invocation.Effort);
        }

        foreach (var dir in ExtractWritableDirs(invocation.AllowedTools))
        {
            psi.ArgumentList.Add("--add-dir");
            psi.ArgumentList.Add(dir);
        }

        foreach (var arg in invocation.ExtraArgs)
            psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add(invocation.PromptContent);

        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";

        return psi;
    }

    internal static IEnumerable<string> ExtractWritableDirs(IReadOnlyList<string> allowedTools)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in allowedTools)
        {
            var match = Regex.Match(tool, @"^(?:Write|Edit)\((.+?)(?:/\*\*?)?\)$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var dir = match.Groups[1].Value;
            if (seen.Add(dir)) yield return dir;
        }
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
