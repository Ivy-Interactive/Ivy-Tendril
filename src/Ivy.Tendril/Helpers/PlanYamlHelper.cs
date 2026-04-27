using System.Text.RegularExpressions;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Helpers;

internal static class PlanYamlHelper
{
    internal static PlanYaml? ReadPlanYaml(string planFolder)
    {
        var yaml = ReadPlanYamlRaw(planFolder);
        if (yaml == null) return null;

        try
        {
            return YamlHelper.Deserializer.Deserialize<PlanYaml>(yaml);
        }
        catch
        {
            return null;
        }
    }

    internal static string? ReadPlanYamlRaw(string planFolder)
    {
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        return File.Exists(planYamlPath) ? FileHelper.ReadAllText(planYamlPath) : null;
    }

    internal static void UpdatePlanYamlFields(string planFolder, params (string field, string value)[] updates)
    {
        var content = ReadPlanYamlRaw(planFolder);
        if (content == null) return;

        foreach (var (field, value) in updates)
        {
            var pattern = $@"(?m)^{Regex.Escape(field)}:\s*.*$";
            var replacement = $"{field}: {value}";
            content = Regex.Replace(content, pattern, replacement);
        }

        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        FileHelper.WriteAllText(planYamlPath, content);
    }

    internal static void SetPlanStateByFolder(string planFolder, string state)
    {
        UpdatePlanYamlFields(planFolder,
            ("state", state),
            ("updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
    }

    internal static string? GetNamedArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    internal static string AllocatePlanId(string plansDir)
    {
        Directory.CreateDirectory(plansDir);
        var counterFile = Path.Combine(plansDir, ".counter");

        // Use file-based locking for cross-process synchronization
        var timeout = TimeSpan.FromSeconds(10);
        var startTime = DateTime.UtcNow;
        var retryDelay = 50; // ms

        while (true)
        {
            try
            {
                // Open counter file with exclusive lock (FileShare.None prevents other processes from accessing)
                using var stream = new FileStream(
                    counterFile,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.None);

                // Read current counter
                var counter = 1;
                if (stream.Length > 0)
                {
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    var text = reader.ReadToEnd().Trim();
                    if (int.TryParse(text, out var parsed))
                        counter = parsed;
                }

                // Skip IDs that already have folders on disk to prevent collisions
                while (Directory.GetDirectories(plansDir, $"{counter.ToString("D5")}-*").Length > 0)
                    counter++;

                var id = counter.ToString("D5");

                // Write incremented counter back to file
                stream.SetLength(0);
                stream.Position = 0;
                using (var writer = new StreamWriter(stream, leaveOpen: true))
                {
                    writer.Write((counter + 1).ToString());
                    writer.Flush();
                }

                return id;
            }
            catch (IOException) when (DateTime.UtcNow - startTime < timeout)
            {
                // Another process holds the lock, retry with exponential backoff
                Thread.Sleep(retryDelay);
                retryDelay = Math.Min(retryDelay * 2, 500);
            }
            catch (IOException)
            {
                throw new TimeoutException(
                    $"Failed to acquire lock on {counterFile} after {timeout.TotalSeconds} seconds. " +
                    "Another process may be holding the lock indefinitely.");
            }
        }
    }

    internal static string? FindPlanFolderById(string plansDir, string? planId)
    {
        if (string.IsNullOrEmpty(planId)) return null;

        try
        {
            var matches = Directory.GetDirectories(plansDir, $"{planId}-*");
            return matches.Length > 0 ? Path.GetFileName(matches[0]) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static string? FindTrashEntryById(string trashDir, string planId)
    {
        if (!Directory.Exists(trashDir)) return null;

        try
        {
            var matches = Directory.GetFiles(trashDir, $"{planId}-*.md");
            return matches.Length > 0 ? matches[0] : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void LogCostToCsv(string planFolder, string jobType, int tokens, double cost)
    {
        if (!Directory.Exists(planFolder)) return;

        var csvPath = Path.Combine(planFolder, "costs.csv");
        if (!File.Exists(csvPath)) FileHelper.WriteAllText(csvPath, "Promptware,Tokens,Cost\n");

        var line = $"{jobType},{tokens},{cost:F4}\n";
        FileHelper.AppendAllText(csvPath, line);
    }

    /// <summary>
    ///     Parses the result status from a verification report file.
    ///     Supports YAML frontmatter format (preferred) and legacy markdown format (fallback).
    /// </summary>
    internal static string? ParseVerificationResultFromReport(string reportContent)
    {
        if (string.IsNullOrWhiteSpace(reportContent)) return null;

        // Try YAML frontmatter first: ---\nresult: Pass\n---
        if (reportContent.StartsWith("---"))
        {
            var endIndex = reportContent.IndexOf("---", 3, StringComparison.Ordinal);
            if (endIndex > 0)
            {
                var frontmatter = reportContent.Substring(3, endIndex - 3);
                foreach (var line in frontmatter.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("result:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = trimmed["result:".Length..].Trim();
                        if (value is "Pass" or "Fail" or "Skipped") return value;
                    }
                }
            }
        }

        // Fallback: legacy markdown format  - **Result:** Pass
        var match = Regex.Match(reportContent, @"^-\s+\*\*Result:\*\*\s+(Pass|Fail|Skipped)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static string? ExtractPlanIdFromFolder(string planFolder)
    {
        var folderName = Path.GetFileName(planFolder);
        var dashIdx = folderName.IndexOf('-');
        return dashIdx > 0 ? folderName[..dashIdx] : null;
    }

    internal static string? ExtractSafeTitleFromFolder(string planFolder)
    {
        var folderName = Path.GetFileName(planFolder);
        var dashIdx = folderName.IndexOf('-');
        return dashIdx > 0 ? folderName[(dashIdx + 1)..] : null;
    }
}
