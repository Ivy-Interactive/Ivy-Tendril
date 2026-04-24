using System.Diagnostics;

namespace Ivy.Tendril.Services.Agents;

public class GeminiAgentProvider : IAgentProvider
{
    public string Name => "gemini";
    public bool UsesStdinPrompt => true;

    public ProcessStartInfo BuildProcessStart(AgentInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gemini",
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

        psi.ArgumentList.Add("--yolo");

        if (!string.IsNullOrEmpty(invocation.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(invocation.Model);
        }

        foreach (var dir in CodexAgentProvider.ExtractWritableDirs(invocation.AllowedTools))
        {
            psi.ArgumentList.Add("--include-directories");
            psi.ArgumentList.Add(dir);
        }

        foreach (var arg in invocation.ExtraArgs)
            psi.ArgumentList.Add(arg);

        // Prompt is piped via stdin to avoid Windows command line length limits.
        // --prompt triggers headless mode; stdin content is read first.
        psi.ArgumentList.Add("--prompt");
        psi.ArgumentList.Add(" ");

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
