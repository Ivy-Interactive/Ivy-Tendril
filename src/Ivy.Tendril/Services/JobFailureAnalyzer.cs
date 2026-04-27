using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services;

internal static class JobFailureAnalyzer
{
    internal static string ExtractFailureReason(List<string> outputLines, string jobType)
    {
        if (outputLines.Count == 0)
            return "Unknown error (exit code non-zero)";

        // 1. Check for PowerShell terminating errors
        var psError = FindPattern(outputLines, new[] {
            @"At line:\d+",
            @"CategoryInfo\s+:",
            @"TerminatingError\(",
            "ScriptHalted",
            @"^\s*\+\s+CategoryInfo",
        });
        if (psError != null) return psError;

        // 2. Check for Claude API errors (from JSON stream)
        var apiError = FindPattern(outputLines, new[] {
            @"""type"":\s*""error""",
            @"""error"":\s*\{",
            "rate_limit_error",
            "overloaded_error",
            "authentication_error",
        });
        if (apiError != null) return ParseClaudeApiError(apiError);

        // 3. Check for CreatePlan-specific failures and failure artifacts
        if (jobType == Constants.JobTypes.CreatePlan)
        {
            var makePlanError = FindPattern(outputLines, new[] {
                "ERROR: Plan",
                "WARNING: TENDRIL_HOME",
                "Failed to parse",
                "Repository path does not exist",
            });
            if (makePlanError != null) return makePlanError;

            var failureArtifactMessage = TryReadFailureArtifact(outputLines);
            if (failureArtifactMessage != null) return failureArtifactMessage;
        }

        // 4. Check for validation/assertion failures
        var validationError = FindPattern(outputLines, new[] {
            @"validation\s+failed",
            @"assertion\s+failed",
            "Repository path does not exist",
            @"\[stderr\].*(?-i)ERROR:",
        });
        if (validationError != null) return validationError;

        // 5. Search from end for stderr lines
        var stderrLines = new List<string>();
        for (var i = outputLines.Count - 1; i >= 0 && stderrLines.Count < 3; i--)
        {
            var line = outputLines[i];
            if (line.StartsWith("[stderr] "))
            {
                var content = line["[stderr] ".Length..].Trim();
                if (content.Length > 0 && !IsProgressMessage(content))
                    stderrLines.Insert(0, content);
            }
        }

        if (stderrLines.Count > 0)
            return SanitizeForDisplay(string.Join(" | ", stderrLines));

        // 6. Fall back to last non-progress line
        for (var i = outputLines.Count - 1; i >= 0; i--)
        {
            var trimmed = outputLines[i].Trim();
            if (trimmed.Length > 0 && !IsProgressMessage(trimmed))
                return SanitizeForDisplay(trimmed);
        }

        return "Unknown error (exit code non-zero)";
    }

    internal static string SanitizeForDisplay(string text)
    {
        text = Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", "");
        text = Regex.Replace(text, @"[\x00-\x1F]", " ");
        text = Regex.Replace(text, " {2,}", " ");
        text = text.Trim();

        if (text.Length > 200)
            text = text[..200] + "...";

        return text;
    }

    private static bool IsProgressMessage(string line)
    {
        var progressPatterns = new[] {
            "Creating Plan", "Executing Plan", "Building", "Researching",
            "Reading", "Analyzing", "Writing", "Searching",
            @"^\d+%", @"^\[.*\]\s+(Starting|Running|Completed)",
        };

        return progressPatterns.Any(p => Regex.IsMatch(line, p, RegexOptions.IgnoreCase));
    }

    private static string? FindPattern(List<string> lines, string[] patterns)
    {
        for (var i = lines.Count - 1; i >= Math.Max(0, lines.Count - 50); i--)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase))
                {
                    var start = Math.Max(0, i - 2);
                    var end = Math.Min(lines.Count - 1, i + 1);
                    var context = string.Join(" | ", lines.Skip(start).Take(end - start + 1));
                    return SanitizeForDisplay(context);
                }
            }
        }
        return null;
    }

    private static string ParseClaudeApiError(string jsonErrorLine)
    {
        try
        {
            var match = Regex.Match(jsonErrorLine, @"""message"":\s*""([^""]+)""");
            if (match.Success)
                return $"Claude API: {match.Groups[1].Value}";
        }
        catch { }

        return "Claude API error (see output for details)";
    }

    internal static string? TryReadFailureArtifact(List<string> outputLines)
    {
        try
        {
            var artifactLine = outputLines.FirstOrDefault(line =>
                line.Contains("Failure artifact written:") && line.Contains("Failed"));

            if (artifactLine == null) return null;

            var pathMatch = Regex.Match(artifactLine, @"Failure artifact written:\s*(.+)");
            if (!pathMatch.Success) return null;

            var artifactPath = pathMatch.Groups[1].Value.Trim();
            if (!File.Exists(artifactPath)) return null;

            var lines = File.ReadAllLines(artifactPath);
            var errorOutputSection = false;
            var errorLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("## Error Output"))
                {
                    errorOutputSection = true;
                    continue;
                }
                if (line.StartsWith("## Investigation Steps"))
                {
                    break;
                }
                if (errorOutputSection && !line.StartsWith("```"))
                {
                    errorLines.Add(line.Trim());
                }
            }

            if (errorLines.Count > 0)
            {
                var summary = string.Join(" | ", errorLines.Take(3).Where(l => l.Length > 0));
                return string.IsNullOrWhiteSpace(summary) ? null : SanitizeForDisplay(summary);
            }
        }
        catch { }

        return null;
    }
}
