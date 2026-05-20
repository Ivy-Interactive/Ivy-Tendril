using System.Diagnostics;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Agents;

public class GeminiAgentProvider : IAgentProvider
{
    public string Name => "gemini";
    public bool UsesStdinPrompt => true;

    public AgentOnboardingInfo OnboardingInfo => new(
        "Gemini CLI", "https://github.com/google-gemini/gemini-cli", "--version",
        () =>
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(home, ".gemini", "oauth_creds.json");
            return ProcessCheckHelper.CheckFileAuth(path, minSize: 0);
        },
        "Sign in to Gemini");

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

        var hasWriteTools = invocation.AllowedTools.Count == 0 || invocation.AllowedTools.Any(t =>
            t.StartsWith("Write", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Edit", StringComparison.OrdinalIgnoreCase));

        psi.ArgumentList.Add("--approval-mode");
        psi.ArgumentList.Add(hasWriteTools ? "yolo" : "plan");

        psi.ArgumentList.Add("--skip-trust");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

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

        psi.ArgumentList.Add("--prompt");
        psi.ArgumentList.Add(" ");

        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";
        psi.Environment["GEMINI_CLI_TRUST_WORKSPACE"] = "true";

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
