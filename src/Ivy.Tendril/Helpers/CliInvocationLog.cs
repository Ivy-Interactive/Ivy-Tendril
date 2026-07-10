using System.Text.Json;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Appends one JSONL line per <c>tendril</c> CLI invocation to the path named by <c>TENDRIL_CLI_LOG</c>
/// (set from <c>tendril promptware run --cli-log</c>). A test/debug instrument for observing which CLI
/// commands an agent ran — it is not a job artifact and does not live in <c>&lt;TendrilHome&gt;/Jobs/</c>.
/// </summary>
/// <remarks>
/// Only the E2E suite turns this on, and only for <c>promptware run</c>: <c>JobLauncher</c> never sets the
/// variable, so app-launched jobs produce no cli-log. It also relies on <c>TENDRIL_CLI_LOG</c> reaching the
/// agent's nested <c>tendril</c> calls, which AGENTS.md documents as unreliable in production; the E2E
/// harness gets away with it because it spawns the CLI as a direct child. Promoting this to a real job
/// artifact would mean proving that propagation first.
/// </remarks>
public static class CliInvocationLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Append(string logPath, string command, int exitCode, double durationMs)
    {
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                FileHelper.EnsureDirectory(dir);
            var entry = new CliLogEntry(DateTime.UtcNow.ToString("O"), command, exitCode, durationMs);
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(logPath, line + "\n");
        }
        catch { /* Best-effort */ }
    }

    public record CliLogEntry(string Timestamp, string Command, int ExitCode, double DurationMs);
}
