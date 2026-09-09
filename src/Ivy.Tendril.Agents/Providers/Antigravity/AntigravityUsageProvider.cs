using System.Globalization;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public delegate Task<(int ExitCode, string Stdout, string Stderr)> AntigravityCommandRunner(
    string fileName,
    IReadOnlyList<string> arguments,
    TimeSpan? timeout = null,
    CancellationToken ct = default);

public sealed class AntigravityUsageProvider : IAgentUsageProvider
{
    private readonly AntigravityCommandRunner _runner;
    private readonly TimeProvider _timeProvider;

    public string AgentId => Abstractions.AgentId.Antigravity;

    public AntigravityUsageProvider(
        AntigravityCommandRunner? runner = null,
        TimeProvider? timeProvider = null)
    {
        _runner = runner ?? ((fn, args, to, ct) => HealthCheckRunner.RunAsync(fn, args, to, ct));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentUsageSnapshot?> GetUsageAsync(CancellationToken ct = default)
    {
        var callTime = _timeProvider.GetUtcNow();

        try
        {
            var (exitCode, stdout, _) = await _runner(
                "agy",
                ["-p", "/usage", "--output-format", "json", "--print-timeout", "10s"],
                TimeSpan.FromSeconds(15),
                ct).ConfigureAwait(false);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return null;

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            if (!root.TryGetProperty("command", out var cmdProp) ||
                !cmdProp.TryGetProperty("data", out var dataProp) ||
                !dataProp.TryGetProperty("groups", out var groupsProp) ||
                groupsProp.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var candidateBuckets = new List<(string GroupName, int WindowMinutes, double RemainingFraction, DateTimeOffset? ResetTime)>();

            foreach (var group in groupsProp.EnumerateArray())
            {
                var groupName = group.TryGetProperty("name", out var gnProp) ? gnProp.GetString() ?? "" : "";
                if (!group.TryGetProperty("buckets", out var bucketsProp) || bucketsProp.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var bucket in bucketsProp.EnumerateArray())
                {
                    if (!bucket.TryGetProperty("window", out var wProp)) continue;
                    var windowStr = wProp.GetString();
                    var windowMinutes = ParseWindowMinutes(windowStr);
                    if (windowMinutes <= 0) continue;

                    double remainingFraction = 1.0;
                    if (bucket.TryGetProperty("remaining_fraction", out var rfProp) && rfProp.TryGetDouble(out var rfVal))
                        remainingFraction = rfVal;

                    DateTimeOffset? resetTime = null;
                    if (bucket.TryGetProperty("reset_time", out var rtProp) &&
                        rtProp.TryGetDateTimeOffset(out var rtVal))
                    {
                        resetTime = rtVal;
                    }

                    candidateBuckets.Add((groupName, windowMinutes, remainingFraction, resetTime));
                }
            }

            if (candidateBuckets.Count == 0)
                return null;

            var windows = new List<AgentUsageWindow>();
            var losingGroups = new List<string>();

            var windowGroups = candidateBuckets
                .GroupBy(b => b.WindowMinutes)
                .OrderBy(g => g.Key);

            foreach (var wg in windowGroups)
            {
                var worst = wg.OrderBy(b => b.RemainingFraction).First();
                var usedPercent = (1.0 - worst.RemainingFraction) * 100.0;

                windows.Add(new AgentUsageWindow
                {
                    WindowMinutes = worst.WindowMinutes,
                    UsedPercent = usedPercent,
                    ResetsAt = worst.ResetTime,
                });

                if (!string.IsNullOrWhiteSpace(worst.GroupName))
                    losingGroups.Add(worst.GroupName);
            }

            var note = losingGroups.Count > 0
                ? $"from {string.Join(", ", losingGroups.Distinct())}"
                : null;

            return new AgentUsageSnapshot
            {
                AgentId = Abstractions.AgentId.Antigravity,
                Windows = windows,
                CapturedAt = callTime,
                Note = note,
            };
        }
        catch
        {
            return null;
        }
    }

    private static int ParseWindowMinutes(string? window)
    {
        if (string.IsNullOrWhiteSpace(window)) return 0;
        if (window.Equals("5h", StringComparison.OrdinalIgnoreCase)) return 300;
        if (window.Equals("weekly", StringComparison.OrdinalIgnoreCase)) return 10080;

        if (window.EndsWith("m", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(window[..^1], out var m))
            return m;

        if (window.EndsWith("h", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(window[..^1], out var h))
            return h * 60;

        if (window.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(window[..^1], out var d))
            return d * 1440;

        if (int.TryParse(window, out var min))
            return min;

        return 0;
    }
}
