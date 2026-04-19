using Ivy.Tendril.Apps.Plans;

namespace Ivy.Tendril.Services;

/// <summary>
///     Shared utilities for plan CLI commands.
/// </summary>
public static class PlanCommandHelpers
{
    /// <summary>
    ///     Resolves a plan folder path from a plan ID.
    /// </summary>
    public static string ResolvePlanFolder(string planId)
    {
        var plansDirectory = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrWhiteSpace(plansDirectory))
            throw new InvalidOperationException("TENDRIL_HOME environment variable is not set");

        plansDirectory = Path.Combine(plansDirectory, "Plans");
        if (!Directory.Exists(plansDirectory))
            throw new DirectoryNotFoundException($"Plans directory not found: {plansDirectory}");

        var planFolders = Directory.GetDirectories(plansDirectory, $"{planId}-*");
        if (planFolders.Length == 0)
            throw new DirectoryNotFoundException($"Plan {planId} not found in {plansDirectory}");

        if (planFolders.Length > 1)
            throw new InvalidOperationException($"Multiple plan folders found for ID {planId}");

        return planFolders[0];
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
