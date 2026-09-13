using System.Globalization;

namespace Ivy.Tendril.Agents.Helpers;

public static class UsageWindowCalculator
{
    public static string FormatWindow(int minutes)
    {
        if (minutes < 60)
            return $"{minutes}m";
        if (minutes < 1440)
            return $"{minutes / 60}h";
        return $"{minutes / 1440}d";
    }

    public static IReadOnlyList<string> EnumerateRecentFiles(string root, DateTimeOffset since, int maxFiles)
    {
        if (!Directory.Exists(root) || maxFiles <= 0)
            return [];

        return Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
            .Where(path => File.GetLastWriteTimeUtc(path) >= since.UtcDateTime)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(maxFiles)
            .ToList();
    }

    public static string FormatTokens(long? tokens)
    {
        if (!tokens.HasValue) return "0";
        var val = tokens.Value;
        if (val >= 1_000_000)
            return (val / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (val >= 10_000)
            return (val / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return val.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "now";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m";
        return $"{remaining.Minutes}m";
    }

    public static string FormatRelative(DateTimeOffset instant)
    {
        var elapsed = DateTimeOffset.UtcNow - instant;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m ago";
        return "just now";
    }
}
