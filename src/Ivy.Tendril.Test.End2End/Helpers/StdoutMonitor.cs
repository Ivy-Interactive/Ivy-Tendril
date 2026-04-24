using System.Text.RegularExpressions;
using Ivy.Tendril.Test.End2End.Fixtures;

namespace Ivy.Tendril.Test.End2End.Helpers;

public static class StdoutMonitor
{
    private static readonly Regex JobExitPattern =
        new(@"Process exited with code\s+(\d+)", RegexOptions.IgnoreCase);

    private static readonly Regex JobKilledPattern =
        new(@"Process killed after timeout", RegexOptions.IgnoreCase);

    private static readonly Regex AgentErrorPattern =
        new(@"(Agent binary not found|No agent program found|Failed to start process)", RegexOptions.IgnoreCase);

    private static readonly Regex JobFailedPattern =
        new(@"Monitor task completed normally|Job.*Failed|Job.*Timeout", RegexOptions.IgnoreCase);

    public static async Task WaitForJobExit(
        TendrilProcessFixture tendril,
        TimeSpan timeout,
        int fromLine = -1,
        CancellationToken cancellation = default)
    {
        var seenCount = fromLine >= 0 ? fromLine : tendril.StdoutLines.Count;
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();

            var lines = tendril.StdoutLines;
            for (var i = seenCount; i < lines.Count; i++)
            {
                var line = lines[i];
                if (JobExitPattern.IsMatch(line) || JobKilledPattern.IsMatch(line))
                    return;
                if (AgentErrorPattern.IsMatch(line))
                    throw new InvalidOperationException($"Agent error detected: {line}");
            }
            seenCount = lines.Count;

            await Task.Delay(500, cancellation);
        }

        throw new TimeoutException(
            $"No job exit detected within {timeout.TotalSeconds}s.\n" +
            $"Tendril stdout (last 30):\n{string.Join("\n", tendril.StdoutLines.TakeLast(30))}");
    }
}
