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

    private static readonly object PlanIdLock = new();

    internal static string AllocatePlanId(string plansDir)
    {
        Directory.CreateDirectory(plansDir);
        var counterFile = Path.Combine(plansDir, ".counter");

        lock (PlanIdLock)
        {
            var counter = 1;
            if (File.Exists(counterFile))
            {
                var text = File.ReadAllText(counterFile).Trim();
                if (int.TryParse(text, out var parsed)) counter = parsed;
            }

            // Skip IDs that already have folders on disk to prevent collisions
            while (Directory.GetDirectories(plansDir, $"{counter.ToString("D5")}-*").Length > 0)
                counter++;

            var id = counter.ToString("D5");
            File.WriteAllText(counterFile, (counter + 1).ToString());
            return id;
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
}
