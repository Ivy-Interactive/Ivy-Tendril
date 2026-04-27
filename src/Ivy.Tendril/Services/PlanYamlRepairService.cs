using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services;

public static class PlanYamlRepairService
{
    private static readonly HashSet<string> TopLevelKeys = new(StringComparer.Ordinal)
    {
        "state", "project", "level", "title", "sessionId",
        "repos", "created", "updated", "initialPrompt", "sourceUrl",
        "prs", "commits", "verifications", "relatedPlans", "dependsOn",
        "priority", "executionProfile", "recommendations"
    };

    private static readonly HashSet<string> ListKeys = new(StringComparer.Ordinal)
    {
        "repos", "prs", "commits", "verifications", "relatedPlans", "dependsOn",
        "recommendations"
    };

    private static readonly Dictionary<string, (string ItemStartPattern, string SubKeyPattern)> StructuredListKeys = new()
    {
        ["verifications"] = (@"^-\s+name:", "^(name|status):"),
        ["recommendations"] = (@"^-\s+title:", "^(title|description|state|impact|risk|declineReason):")
    };

    public static string RepairPlanYaml(string yaml)
    {
        var repaired = yaml;

        repaired = RepairMultiLineQuotedScalars(repaired);

        repaired = Regex.Replace(repaired, @"(?m)^---[ \t]*(\r?\n|$)", "");

        repaired = Regex.Replace(repaired,
            @"(?m)^(\s*)-\s+name:\s*.+\r?\n\s+path:\s*(.+?)(?:\r?\n\s+(?:branch|prRule):\s*.+)*$",
            "$1- $2");

        repaired = Regex.Replace(repaired,
            @"(?m)^(\s*)-\s+path:\s*(.+?)(?:\r?\n\s+(?:prRule|branch):\s*.+)*$",
            "$1- $2");

        repaired = Regex.Replace(repaired,
            @"(?m)^(\s*)-\s+hash:\s*(.+?)(?:\r?\n\s+(?:repo|message):\s*.+)*$",
            "$1- $2");

        repaired = Regex.Replace(repaired,
            @"(?m)^(\s*)-\s+note:\s*(.+)$",
            "$1- $2");

        repaired = Regex.Replace(repaired,
            @"(?m)^(\s*-\s+(?:""[^""]*""|'[^']*'|%\S+).*)\r?\n\s+branch:\s*.*$",
            "$1");

        repaired = Regex.Replace(repaired, @"(?m)^([^:]+:\s+)""(.+)""(\s*)$", m =>
        {
            var prefix = m.Groups[1].Value;
            var inner = m.Groups[2].Value;
            var suffix = m.Groups[3].Value;
            if (Regex.IsMatch(inner, @"\\[^""\\nt/abfre0 UNLP_xu]"))
            {
                var escaped = inner.Replace("'", "''");
                return $"{prefix}'{escaped}'{suffix}";
            }

            return m.Value;
        });

        repaired = Regex.Replace(repaired, @"(?m)^(\s*-\s+)""(.+)""(\s*)$", m =>
        {
            var prefix = m.Groups[1].Value;
            var inner = m.Groups[2].Value;
            var suffix = m.Groups[3].Value;
            if (Regex.IsMatch(inner, @"\\[^""\\nt/abfre0 UNLP_xu]"))
            {
                var escaped = inner.Replace("'", "''");
                return $"{prefix}'{escaped}'{suffix}";
            }

            return m.Value;
        });

        repaired = Regex.Replace(repaired, @"(?m)^(\s*-\s+)([A-Za-z]:\\[^\s].*)$", m =>
        {
            var prefix = m.Groups[1].Value;
            var path = m.Groups[2].Value.TrimEnd();
            if (path.StartsWith("\"") || path.StartsWith("'")) return m.Value;
            var escaped = path.Replace("'", "''");
            return $"{prefix}'{escaped}'";
        });

        repaired = Regex.Replace(repaired, @"(?m)^(\s*\w+:\s+)(.+)$", m =>
        {
            var prefix = m.Groups[1].Value;
            var value = m.Groups[2].Value.TrimEnd();
            if (value.StartsWith("\"") || value.StartsWith("'") ||
                value.StartsWith("|") || value.StartsWith(">") ||
                value.StartsWith("-") || value.StartsWith("{") || value.StartsWith("["))
                return m.Value;
            if (value.Contains(": "))
            {
                var escaped = value.Replace("'", "''");
                return $"{prefix}'{escaped}'";
            }

            return m.Value;
        });

        repaired = Regex.Replace(
            repaired,
            @"(?m)^(\s*)(repos|commits|prs|verifications|relatedPlans|dependsOn):\s*\r?\n(?!\s*-)",
            "$1$2: []\n");

        repaired = Regex.Replace(
            repaired,
            @"(?m)^(\s*)(repos|commits|prs|verifications|relatedPlans|dependsOn):[ \t]*$",
            "$1$2: []");

        repaired = Regex.Replace(
            repaired,
            @"(?m)^priority:\s*(?!\d).*$",
            "priority: 0");

        return NormalizePlanYamlStructure(repaired);
    }

    private static string NormalizePlanYamlStructure(string yaml)
    {
        var normalized = yaml.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        string? currentListKey = null;
        var inStructuredListItem = false;
        var inListItemBlockScalar = false;
        var inBlockScalar = false;
        var inUnknownKey = false;
        var seenKeys = new HashSet<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                output.Add(inBlockScalar ? "  " : string.Empty);
                continue;
            }

            var detectedKey = TryExtractTopLevelKey(trimmed, out var normalizedTopLevelLine);
            if (inBlockScalar && detectedKey == null)
            {
                output.Add($"  {line}");
                continue;
            }

            if (detectedKey != null)
            {
                if (!seenKeys.Add(detectedKey))
                    continue;

                currentListKey = ListKeys.Contains(detectedKey) ? detectedKey : null;
                inStructuredListItem = false;
                inListItemBlockScalar = false;
                inBlockScalar = false;
                inUnknownKey = false;

                if (currentListKey != null &&
                    Regex.IsMatch(normalizedTopLevelLine, @"^[A-Za-z][A-Za-z0-9]*:\s*\[\]\s*$"))
                    output.Add($"{detectedKey}:");
                else
                    output.Add(normalizedTopLevelLine);

                var scalarValue = normalizedTopLevelLine[(detectedKey.Length + 1)..].Trim();
                if (IsBlockScalarValue(scalarValue))
                    inBlockScalar = true;

                continue;
            }

            if (inUnknownKey)
            {
                if (line != trimmed)
                    continue;
                inUnknownKey = false;
            }

            if (inBlockScalar)
            {
                output.Add($"  {line}");
                continue;
            }

            if (currentListKey != null)
            {
                ProcessListContext(trimmed, line, currentListKey, ref inStructuredListItem,
                    ref inListItemBlockScalar, ref inUnknownKey, ref currentListKey, output);
                continue;
            }

            var unknownKeyMatch = Regex.Match(trimmed, "^([A-Za-z][A-Za-z0-9]+):");
            if (unknownKeyMatch.Success)
            {
                inUnknownKey = true;
                continue;
            }

            output.Add(trimmed);
        }

        return string.Join(Environment.NewLine, output);
    }

    private static void ProcessListContext(
        string trimmed, string line, string currentListKey,
        ref bool inStructuredListItem, ref bool inListItemBlockScalar,
        ref bool inUnknownKey, ref string? currentListKeyRef,
        List<string> output)
    {
        var isStructured = StructuredListKeys.TryGetValue(currentListKey, out var patterns);

        if (trimmed.StartsWith("-"))
        {
            if (isStructured && !Regex.IsMatch(trimmed, patterns.ItemStartPattern))
            {
                inStructuredListItem = false;
                inListItemBlockScalar = false;
                return;
            }

            output.Add($"  {trimmed}");
            inStructuredListItem = isStructured;
            inListItemBlockScalar = false;
        }
        else if (isStructured && inStructuredListItem)
        {
            ProcessStructuredListItem(trimmed, patterns.SubKeyPattern,
                ref inListItemBlockScalar, output);
        }
        else
        {
            var strayKeyMatch = Regex.Match(trimmed, "^([A-Za-z][A-Za-z0-9]+):");
            if (strayKeyMatch.Success && TopLevelKeys.Contains(strayKeyMatch.Groups[1].Value))
            {
                var key = strayKeyMatch.Groups[1].Value;
                currentListKeyRef = ListKeys.Contains(key) ? key : null;
                inStructuredListItem = false;
                inListItemBlockScalar = false;
                output.Add(trimmed);
            }
            else if (strayKeyMatch.Success)
            {
                currentListKeyRef = null;
                inUnknownKey = true;
                inListItemBlockScalar = false;
            }
            else
            {
                output.Add($"  {trimmed}");
            }
        }
    }

    private static void ProcessStructuredListItem(
        string trimmed, string subKeyPattern,
        ref bool inListItemBlockScalar, List<string> output)
    {
        if (inListItemBlockScalar)
        {
            if (Regex.IsMatch(trimmed, subKeyPattern))
            {
                inListItemBlockScalar = false;
                output.Add($"    {trimmed}");
            }
            else
            {
                output.Add($"      {trimmed}");
            }
        }
        else if (Regex.IsMatch(trimmed, subKeyPattern))
        {
            output.Add($"    {trimmed}");
            var subValue = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
            if (IsBlockScalarValue(subValue))
                inListItemBlockScalar = true;
        }
    }

    private static string RepairMultiLineQuotedScalars(string yaml)
    {
        var normalized = yaml.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var result = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^([a-zA-Z]\w*:)\s+([""'])(.*)$");
            if (!m.Success)
            {
                result.Add(lines[i]);
                continue;
            }

            var key = m.Groups[1].Value;
            var quote = m.Groups[2].Value[0];
            var rest = m.Groups[3].Value;

            if (IsScalarQuoteClosed(rest, quote))
            {
                result.Add(lines[i]);
                continue;
            }

            var contentLines = new List<string>();
            if (rest.Length > 0)
                contentLines.Add(rest);

            var j = i + 1;
            while (j < lines.Length)
            {
                var next = lines[j];
                var nextTrimmed = next.TrimStart();

                if (nextTrimmed.Length > 0 && Regex.IsMatch(nextTrimmed, @"^[a-zA-Z]\w*:(\s|$)"))
                    break;

                var nextStripped = next.TrimEnd();
                if (nextStripped.Length > 0 && EndsWithClosingQuote(nextStripped, quote))
                {
                    contentLines.Add(nextStripped[..^1]);
                    j++;
                    break;
                }

                contentLines.Add(next);
                j++;
            }

            while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[^1]))
                contentLines.RemoveAt(contentLines.Count - 1);

            result.Add($"{key} |");
            foreach (var cl in contentLines)
            {
                var unescaped = quote == '"'
                    ? cl.Replace("\\\\", "\x01").Replace("\\\"", "\"").Replace("\x01", "\\")
                    : cl.Replace("''", "'");
                result.Add($"  {unescaped}");
            }

            i = j - 1;
        }

        return string.Join("\n", result);
    }

    private static bool IsScalarQuoteClosed(string afterOpenQuote, char quote)
    {
        if (quote == '"')
        {
            for (var k = 0; k < afterOpenQuote.Length; k++)
            {
                if (afterOpenQuote[k] == '\\') { k++; continue; }
                if (afterOpenQuote[k] == '"') return true;
            }
            return false;
        }

        for (var k = 0; k < afterOpenQuote.Length; k++)
        {
            if (afterOpenQuote[k] != '\'') continue;
            if (k + 1 < afterOpenQuote.Length && afterOpenQuote[k + 1] == '\'') { k++; continue; }
            return true;
        }
        return false;
    }

    private static bool EndsWithClosingQuote(string line, char quote)
    {
        if (line.Length == 0 || line[^1] != quote) return false;
        if (quote == '"')
        {
            var backslashes = 0;
            for (var k = line.Length - 2; k >= 0 && line[k] == '\\'; k--)
                backslashes++;
            return backslashes % 2 == 0;
        }

        var quotes = 0;
        for (var k = line.Length - 1; k >= 0 && line[k] == '\''; k--)
            quotes++;
        return quotes % 2 == 1;
    }

    private static bool IsBlockScalarValue(string value)
    {
        return value is "|" or "|-" or ">" or ">-";
    }

    private static string? TryExtractTopLevelKey(string trimmedLine, out string normalizedLine)
    {
        normalizedLine = trimmedLine;

        var keyMatch = Regex.Match(trimmedLine, "^([A-Za-z][A-Za-z0-9]*):");
        if (keyMatch.Success && TopLevelKeys.Contains(keyMatch.Groups[1].Value))
            return keyMatch.Groups[1].Value;

        var quotedMatch = Regex.Match(trimmedLine, @"^'([A-Za-z][A-Za-z0-9]*):\s*(.*)'$");
        if (!quotedMatch.Success || !TopLevelKeys.Contains(quotedMatch.Groups[1].Value))
            return null;

        var key = quotedMatch.Groups[1].Value;
        var value = quotedMatch.Groups[2].Value.Replace("''", "'");
        normalizedLine = $"{key}: {value}".TrimEnd();
        return key;
    }
}
