using Ivy.Tendril.Apps.Plans;

namespace Ivy.Tendril.Services;

/// <summary>
///     Shared utilities for plan CLI commands.
/// </summary>
public static class PlanCommandHelpers
{
    /// <summary>
    ///     Resolves a plan folder path from a plan ID.
    ///     Accepts: full path, folder name (e.g. "00015-Title"), zero-padded ID ("00015"), or bare number ("15").
    /// </summary>
    public static string ResolvePlanFolder(string planId)
    {
        var plansDirectory = GetPlansDirectory();

        var normalized = NormalizePlanId(planId, plansDirectory);

        var planFolders = Directory.GetDirectories(plansDirectory, $"{normalized}-*");
        if (planFolders.Length == 0)
            throw new DirectoryNotFoundException($"Plan {normalized} not found in {plansDirectory}");

        if (planFolders.Length > 1)
            throw new InvalidOperationException($"Multiple plan folders found for ID {normalized}");

        return planFolders[0];
    }

    internal static string NormalizePlanId(string input, string plansDirectory)
    {
        input = input.Trim().TrimEnd('/', '\\');

        // Full path: extract folder name then ID
        if (Path.IsPathRooted(input))
            input = Path.GetFileName(input);

        // Folder name like "00015-Title": extract numeric prefix
        var dashIndex = input.IndexOf('-');
        if (dashIndex > 0 && int.TryParse(input[..dashIndex], out _))
            return input[..dashIndex];

        // Already a zero-padded or bare number
        if (int.TryParse(input, out var num))
            return num.ToString("D5");

        return input;
    }

    /// <summary>
    ///     Resolves the plans directory from TENDRIL_PLANS or TENDRIL_HOME/Plans.
    /// </summary>
    public static string GetPlansDirectory()
    {
        var plans = Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim();
        if (!string.IsNullOrEmpty(plans))
        {
            if (!Directory.Exists(plans))
                throw new DirectoryNotFoundException($"Plans directory not found: {plans}");
            return plans;
        }

        var home = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("TENDRIL_HOME environment variable is not set");

        var plansDirectory = Path.Combine(home, "Plans");
        if (!Directory.Exists(plansDirectory))
            throw new DirectoryNotFoundException($"Plans directory not found: {plansDirectory}");

        return plansDirectory;
    }

    /// <summary>
    ///     Reads a plan.yaml file and deserializes it.
    /// </summary>
    public static PlanYaml ReadPlan(string planFolder)
    {
        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException($"plan.yaml not found at {yamlPath}");

        var content = File.ReadAllText(yamlPath);
        var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(content);
        if (plan == null)
            throw new InvalidOperationException($"Failed to deserialize plan.yaml at {yamlPath}");

        return plan;
    }

    /// <summary>
    ///     Writes a plan.yaml file atomically.
    ///     Validates the plan, writes to temp file, reads back for verification, then atomically moves to target.
    /// </summary>
    public static void WritePlan(string planFolder, PlanYaml plan)
    {
        // Validate before writing
        PlanValidationService.Validate(plan);

        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        var tempPath = Path.Combine(planFolder, $"plan.yaml.tmp.{Guid.NewGuid():N}");

        try
        {
            // Serialize to temp file
            var yaml = YamlHelper.Serializer.Serialize(plan);
            File.WriteAllText(tempPath, yaml);

            // Read back and validate
            var content = File.ReadAllText(tempPath);
            var roundTrip = YamlHelper.Deserializer.Deserialize<PlanYaml>(content);
            if (roundTrip == null)
                throw new InvalidOperationException("Failed to deserialize temp file after writing");

            PlanValidationService.Validate(roundTrip);

            // Atomic move
            File.Move(tempPath, yamlPath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }
}
