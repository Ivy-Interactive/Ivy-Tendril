namespace Ivy.Tendril.Test.End2End.Helpers;

public static class LogAssertions
{
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
