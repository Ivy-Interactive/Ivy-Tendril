using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services;

public class OnboardingAuthRunner : IOnboardingAuthRunner
{
    private record AuthSpec(string FileName, string Arguments, Regex? CodeRegex, Regex UrlRegex);

    private static readonly Regex GenericUrlRegex = new(@"https?://[^\s""']+", RegexOptions.Compiled);
    private static readonly Regex GhCodeRegex = new(@"\b[A-Z0-9]{4}-[A-Z0-9]{4}\b", RegexOptions.Compiled);

    private static AuthSpec? GetSpec(string toolKey) => toolKey switch
    {
        "gh" => new("gh", "auth login --web --hostname github.com --git-protocol https", GhCodeRegex, GenericUrlRegex),
        "claude" => new("claude", "/login", null, GenericUrlRegex),
        "codex" => new("codex", "login", null, GenericUrlRegex),
        "gemini" => new("gemini", "", null, GenericUrlRegex),
        "copilot" => new("copilot", "", null, GenericUrlRegex),
        _ => null
    };

    public async Task<bool> RunAuthAsync(string toolKey, IClientProvider client, Action<string> onCode, CancellationToken ct)
    {
        var spec = GetSpec(toolKey);
        if (spec is null) return false;

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : spec.FileName,
            Arguments = OperatingSystem.IsWindows()
                ? $"/S /c \"{spec.FileName} {spec.Arguments}\""
                : spec.Arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return false;

            var urlOpened = false;
            var lockObj = new object();

            void Handle(string? line)
            {
                if (string.IsNullOrEmpty(line)) return;

                if (spec.CodeRegex is not null)
                {
                    var codeMatch = spec.CodeRegex.Match(line);
                    if (codeMatch.Success)
                    {
                        try { onCode(codeMatch.Value); } catch { /* ignore UI callback errors */ }
                    }
                }

                lock (lockObj)
                {
                    if (urlOpened) return;
                    var urlMatch = spec.UrlRegex.Match(line);
                    if (!urlMatch.Success) return;
                    urlOpened = true;
                    try { client.OpenUrl(urlMatch.Value); } catch { /* ignore */ }
                }
            }

            proc.OutputDataReceived += (_, e) => Handle(e.Data);
            proc.ErrorDataReceived += (_, e) => Handle(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Some CLIs (notably gh) prompt "Press Enter to open ..." — pre-feed newlines so the
            // prompt clears even though we're not on a TTY.
            try
            {
                await proc.StandardInput.WriteLineAsync();
                await proc.StandardInput.WriteLineAsync();
                proc.StandardInput.Close();
            }
            catch { /* stdin may already be closed */ }

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
