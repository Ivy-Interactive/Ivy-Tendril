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
        var logFile = JobLogs(tendrilHome).FirstOrDefault(f => IsForPlan(f, planId));
        if (logFile == null) return null;

        if (File.Exists(logFile))
            return File.ReadAllText(logFile);

        var rawLog = Path.ChangeExtension(logFile, ".raw.jsonl");
        return File.Exists(rawLog) ? File.ReadAllText(rawLog) : null;
    }

    public static void AssertLogContains(string tendrilHome, string planId, string expectedText)
    {
        var log = GetJobLog(tendrilHome, planId);
        Assert.NotNull(log);
        Assert.Contains(expectedText, log!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Job Logs in <c>&lt;TendrilHome&gt;/Jobs/</c>. Excludes <c>.prompt.md</c>, which is the verbatim
    /// agent prompt rather than a log — its prose routinely mentions "error" and would trip AssertNoErrors.
    /// </summary>
    private static IEnumerable<string> JobLogs(string tendrilHome)
    {
        var jobsDir = Path.Combine(tendrilHome, "Jobs");
        if (!Directory.Exists(jobsDir)) return [];

        return Directory.GetFiles(jobsDir, "*.md")
            .Where(f => !f.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);
    }

    /// <summary>Job log stems are <c>{jobId}-{planId}-{promptware}</c>.</summary>
    private static bool IsForPlan(string logFile, string planId)
    {
        var parts = Path.GetFileNameWithoutExtension(logFile).Split('-');
        return parts.Length >= 3 && parts[1] == planId;
    }

    private static IEnumerable<string> FindLogFiles(string tendrilHome) => JobLogs(tendrilHome);
}
