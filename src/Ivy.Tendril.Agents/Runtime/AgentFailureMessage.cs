using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

internal static class AgentFailureMessage
{
    private const int MaxStderrLineLength = 500;

    internal static string? Describe(
        bool idleTimeoutFired,
        TimeSpan? idleTimeout,
        string? abortReason,
        int exitCode,
        SessionState state,
        IReadOnlyList<string> stderrTail)
    {
        if (idleTimeoutFired && idleTimeout.HasValue)
            return $"Agent timed out: no output received for {FormatDuration(idleTimeout.Value)} (idle timeout threshold exceeded).";

        if (!string.IsNullOrWhiteSpace(abortReason))
            return abortReason;

        if (state == SessionState.Stopped)
            return $"Agent process was stopped before it completed (exit code {exitCode}).";

        if (exitCode != 0)
        {
            var lastStderrLine = stderrTail.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return lastStderrLine is not null
                ? $"Agent process exited with code {exitCode}: {Truncate(lastStderrLine)}"
                : $"Agent process exited with code {exitCode} without reporting a result.";
        }

        return null;
    }

    private static string Truncate(string line)
        => line.Length > MaxStderrLineLength
            ? line[..MaxStderrLineLength] + "..."
            : line;

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration >= TimeSpan.FromMinutes(1))
        {
            var minutes = (int)duration.TotalMinutes;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        var seconds = (int)duration.TotalSeconds;
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
