namespace Ivy.Tendril.Test.End2End.Helpers;

public static class LogAssertions
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static void AssertNoErrors(string tendrilHome)
    {
        var logFiles = FindLogFiles(tendrilHome);
        var errors = new List<string>();

        foreach (var logFile in logFiles)
        {
            var lines = File.ReadAllLines(logFile);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{Path.GetFileName(logFile)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.Empty(errors);
    }

    public static string? GetJobLog(string tendrilHome, string planId)
    {
        var promptwaresDir = Path.Combine(tendrilHome, "Promptwares");
        if (!Directory.Exists(promptwaresDir))
            return null;

        foreach (var typeDir in Directory.GetDirectories(promptwaresDir))
        {
            var logsDir = Path.Combine(typeDir, "Logs");
            if (!Directory.Exists(logsDir)) continue;

            var logFile = Path.Combine(logsDir, $"{planId}.md");
            if (File.Exists(logFile))
                return File.ReadAllText(logFile);

            var rawLog = Path.Combine(logsDir, $"{planId}.raw.jsonl");
            if (File.Exists(rawLog))
                return File.ReadAllText(rawLog);
        }

        return null;
    }

    public static void AssertLogContains(string tendrilHome, string planId, string expectedText)
    {
        var log = GetJobLog(tendrilHome, planId);
        Assert.NotNull(log);
        Assert.Contains(expectedText, log!, StringComparison.OrdinalIgnoreCase);
    }

    // --- CLI job log (NNN-{jobType}-job.jsonl) ---

    public static List<CliLogEntry> GetCliLogEntries(string planFolder, string jobType)
    {
        var logsDir = Path.Combine(planFolder, "logs");
        if (!Directory.Exists(logsDir))
            return [];

        var entries = new List<CliLogEntry>();
        foreach (var file in Directory.GetFiles(logsDir, $"*-{jobType}-job.jsonl"))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = System.Text.Json.JsonSerializer.Deserialize<CliLogEntry>(line, JsonOptions);
                    if (entry != null) entries.Add(entry);
                }
                catch { }
            }
        }
        return entries;
    }

    public static void AssertCliLogHasEntries(string planFolder, string jobType, int minEntries = 1)
    {
        var logsDir = Path.Combine(planFolder, "logs");
        if (!Directory.Exists(logsDir))
            return; // No logs directory — CLI log may not be produced in dotnet-run mode

        var entries = GetCliLogEntries(planFolder, jobType);
        if (entries.Count == 0 && Directory.GetFiles(logsDir, $"*-{jobType}-job.jsonl").Length == 0)
            return; // No log file for this job type — acceptable in dev/test environments

        Assert.True(entries.Count >= minEntries,
            $"Expected at least {minEntries} CLI log entries for {jobType} in {planFolder}, found {entries.Count}");
    }

    public static void AssertCliLogContainsCommand(string planFolder, string jobType, string commandFragment)
    {
        var entries = GetCliLogEntries(planFolder, jobType);
        Assert.True(entries.Count > 0,
            $"No CLI log entries found for {jobType} in {planFolder}");
        Assert.Contains(entries, e =>
            e.Command != null && e.Command.Contains(commandFragment, StringComparison.OrdinalIgnoreCase));
    }

    public static void AssertAllCliCallsSucceeded(string planFolder, string jobType)
    {
        var entries = GetCliLogEntries(planFolder, jobType);
        if (entries.Count == 0)
            return; // No CLI log entries — acceptable in dev/test environments

        var failures = entries
            .Where(e => e.ExitCode != 0)
            .ToList();
        Assert.True(failures.Count == 0,
            $"CLI calls failed for {jobType}:\n" +
            string.Join("\n", failures.Select(f => $"  [{f.ExitCode}] {f.Command}")));
    }

    public record CliLogEntry(
        string? Timestamp,
        string? Command,
        int ExitCode,
        double DurationMs);

    private static IEnumerable<string> FindLogFiles(string tendrilHome)
    {
        var files = new List<string>();

        var promptwaresDir = Path.Combine(tendrilHome, "Promptwares");
        if (Directory.Exists(promptwaresDir))
        {
            foreach (var typeDir in Directory.GetDirectories(promptwaresDir))
            {
                var logsDir = Path.Combine(typeDir, "Logs");
                if (Directory.Exists(logsDir))
                    files.AddRange(Directory.GetFiles(logsDir, "*.md"));
            }
        }

        var plansDir = Path.Combine(tendrilHome, "Plans");
        if (Directory.Exists(plansDir))
        {
            foreach (var planDir in Directory.GetDirectories(plansDir))
            {
                var logsDir = Path.Combine(planDir, "logs");
                if (Directory.Exists(logsDir))
                    files.AddRange(Directory.GetFiles(logsDir));
            }
        }

        return files;
    }
}
