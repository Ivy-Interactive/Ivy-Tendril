using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services.Agents;

public class CopilotAgentProvider : IAgentProvider
{
    public string Name => "copilot";
    public bool UsesStdinPrompt => false;

    private static readonly string ShellToolName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "bash";

    private static readonly Dictionary<string, string> ToolNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Read"] = "view",
        ["Write"] = "apply_patch",
        ["Edit"] = "apply_patch",
        ["Bash"] = ShellToolName,
        ["Glob"] = "glob",
        ["Grep"] = "rg",
        ["WebFetch"] = "web_fetch",
        ["WebSearch"] = "web_fetch",
    };

    public ProcessStartInfo BuildProcessStart(AgentInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "copilot",
            WorkingDirectory = invocation.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(invocation.PromptContent);
        psi.ArgumentList.Add("--allow-all-paths");
        psi.ArgumentList.Add("--allow-all-urls");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-s");

        if (invocation.AllowedTools.Count > 0)
        {
            var copilotTools = TranslateTools(invocation.AllowedTools);
            if (copilotTools.Count > 0)
            {
                psi.ArgumentList.Add("--available-tools");
                psi.ArgumentList.Add(string.Join(",", copilotTools));
            }
            psi.ArgumentList.Add("--allow-all-tools");
        }
        else
        {
            psi.ArgumentList.Add("--allow-all-tools");
        }

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
            psi.ArgumentList.Add("--name");
            psi.ArgumentList.Add(invocation.SessionId);
        }

        foreach (var dir in CodexAgentProvider.ExtractWritableDirs(invocation.AllowedTools))
        {
            psi.ArgumentList.Add("--add-dir");
            psi.ArgumentList.Add(dir);
        }

        foreach (var arg in invocation.ExtraArgs)
            psi.ArgumentList.Add(arg);

        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";

        return psi;
    }

    internal static IReadOnlyList<string> TranslateTools(IReadOnlyList<string> claudeTools)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in claudeTools)
        {
            var baseName = Regex.Match(tool, @"^(\w+)").Groups[1].Value;
            if (ToolNameMap.TryGetValue(baseName, out var copilotName))
                result.Add(copilotName);
        }
        return result.ToList();
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
