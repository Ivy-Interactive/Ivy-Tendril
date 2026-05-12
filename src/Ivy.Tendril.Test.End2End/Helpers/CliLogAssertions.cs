using System.Text.Json;

namespace Ivy.Tendril.Test.End2End.Helpers;

public record CliLogEntry(string Timestamp, string Command, int ExitCode, double DurationMs);

public static class CliLogAssertions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<CliLogEntry> ReadLog(string cliLogPath)
    {
        if (!File.Exists(cliLogPath))
            return [];

        var entries = new List<CliLogEntry>();
        foreach (var line in File.ReadAllLines(cliLogPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<CliLogEntry>(line, JsonOptions);
            if (entry != null) entries.Add(entry);
        }
        return entries;
    }

    public static void AssertCommandCalled(string cliLogPath, string commandFragment)
    {
        var entries = ReadLog(cliLogPath);
        Assert.True(
            entries.Any(e => e.Command.Contains(commandFragment, StringComparison.OrdinalIgnoreCase)),
            $"Expected a CLI call containing '{commandFragment}'. " +
            $"Actual calls ({entries.Count}): [{string.Join(", ", entries.Select(e => $"\"{e.Command}\""))}]");
    }

    public static void AssertCommandCalledWithArgs(string cliLogPath, string commandFragment, params string[] expectedArgs)
    {
        var entries = ReadLog(cliLogPath);
        var matching = entries.Where(e => e.Command.Contains(commandFragment, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(matching.Count > 0,
            $"Expected a CLI call containing '{commandFragment}'. " +
            $"Actual calls ({entries.Count}): [{string.Join(", ", entries.Select(e => $"\"{e.Command}\""))}]");

        foreach (var arg in expectedArgs)
        {
            Assert.True(
                matching.Any(e => e.Command.Contains(arg, StringComparison.OrdinalIgnoreCase)),
                $"Expected '{commandFragment}' call to contain arg '{arg}'. " +
                $"Matching calls: [{string.Join(", ", matching.Select(e => $"\"{e.Command}\""))}]");
        }
    }

    public static void AssertCommandNotCalled(string cliLogPath, string commandFragment)
    {
        var entries = ReadLog(cliLogPath);
        Assert.True(
            !entries.Any(e => e.Command.Contains(commandFragment, StringComparison.OrdinalIgnoreCase)),
            $"Expected no CLI call containing '{commandFragment}', but found one. " +
            $"Calls: [{string.Join(", ", entries.Where(e => e.Command.Contains(commandFragment, StringComparison.OrdinalIgnoreCase)).Select(e => $"\"{e.Command}\""))}]");
    }

    public static void AssertAllCommandsSucceeded(string cliLogPath)
    {
        var entries = ReadLog(cliLogPath);
        var failed = entries.Where(e => e.ExitCode != 0).ToList();
        Assert.True(failed.Count == 0,
            $"Expected all CLI calls to succeed, but {failed.Count} failed: " +
            $"[{string.Join(", ", failed.Select(e => $"\"{e.Command}\" (exit={e.ExitCode})"))}]");
    }

    public static void AssertMinimumCalls(string cliLogPath, string commandFragment, int minCount)
    {
        var entries = ReadLog(cliLogPath);
        var count = entries.Count(e => e.Command.Contains(commandFragment, StringComparison.OrdinalIgnoreCase));
        Assert.True(count >= minCount,
            $"Expected at least {minCount} call(s) containing '{commandFragment}', found {count}. " +
            $"All calls: [{string.Join(", ", entries.Select(e => $"\"{e.Command}\""))}]");
    }
}
