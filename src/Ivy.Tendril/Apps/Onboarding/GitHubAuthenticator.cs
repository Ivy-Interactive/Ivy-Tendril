using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Apps.Onboarding;

internal static class GitHubAuthenticator
{
    public const string DeviceFlowUrl = "https://github.com/login/device";

    private static readonly Regex CodePattern = new(
        @"one-time code:\s+([A-Z0-9]+-[A-Z0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<(bool Success, string Message)> AuthenticateAsync(
        Action<string> onCodeDetected,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "gh",
                Arguments = OperatingSystem.IsWindows()
                    ? "/S /c \"gh auth login --web -h github.com -p https\""
                    : "auth login --web -h github.com -p https",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return (false, "Failed to start gh.");

            // gh's --web flow has up to two interactive prompts before it starts
            // polling: an optional "Authenticate Git with your GitHub credentials?"
            // (default Y) and a mandatory "Press Enter to open ... in your browser".
            // Pre-feed two newlines so both default through; surplus is harmless.
            try
            {
                await proc.StandardInput.WriteLineAsync();
                await proc.StandardInput.WriteLineAsync();
                await proc.StandardInput.FlushAsync();
            }
            catch { /* best-effort */ }

            var codeReported = 0;
            var output = new StringBuilder();
            var sync = new object();

            void Handle(string? line)
            {
                if (line is null) return;
                lock (sync) { output.AppendLine(line); }
                if (Interlocked.CompareExchange(ref codeReported, 1, 0) != 0) return;
                var m = CodePattern.Match(line);
                if (!m.Success)
                {
                    Interlocked.Exchange(ref codeReported, 0);
                    return;
                }
                try { onCodeDetected(m.Groups[1].Value); }
                catch { /* don't let UI errors kill the auth flow */ }
            }

            proc.OutputDataReceived += (_, e) => Handle(e.Data);
            proc.ErrorDataReceived += (_, e) => Handle(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return (false, "Authentication timed out after 10 minutes. Try again, or run `gh auth login` from a terminal.");
            }

            string combined;
            lock (sync) { combined = output.ToString().Trim(); }

            if (proc.ExitCode != 0)
            {
                return (false, string.IsNullOrWhiteSpace(combined)
                    ? $"gh auth login exited with code {proc.ExitCode}."
                    : $"gh auth login failed: {combined}");
            }
            return (true, combined);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
