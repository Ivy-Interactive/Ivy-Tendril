using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

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
        // If the input is already a valid plan folder path, use it directly.
        // This allows agents to pass the full Directory path from `tendril plan create`
        // without relying on env var inheritance for resolution.
        if (Path.IsPathRooted(planId) && Directory.Exists(planId) && File.Exists(Path.Combine(planId, "plan.yaml")))
            return planId;

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

        // Full path: extract folder name then ID (handles both Windows and Unix paths)
        if (Path.IsPathRooted(input) || (input.Length >= 3 && input[1] == ':' && (input[2] == '\\' || input[2] == '/')))
            input = PathHelper.GetFileNameCrossPlatform(input);

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
    ///     Resolves any plan reference format to the canonical folder name (not the full path).
    ///     Accepts: full path, folder name (e.g. "00015-Title"), zero-padded ID ("00015"), or bare number ("15").
    /// </summary>
    public static string ResolvePlanFolderName(string planRef)
    {
        var fullPath = ResolvePlanFolder(planRef);
        return Path.GetFileName(fullPath);
    }

    /// <summary>
    ///     Resolves the plans directory from an explicit override, TENDRIL_PLANS, or TENDRIL_HOME/Plans.
    /// </summary>
    public static string GetPlansDirectory(string? explicitPlansDir = null)
    {
        if (!string.IsNullOrEmpty(explicitPlansDir))
        {
            if (!Directory.Exists(explicitPlansDir))
                Directory.CreateDirectory(explicitPlansDir);
            return explicitPlansDir;
        }

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
    ///     Seeds <paramref name="plan"/>.Verifications with the full project verification set, in
    ///     project-config order (the order they run in). Each verification gets its explicit override
    ///     status if one was supplied, otherwise the default: Required → Pending, Optional → Skipped.
    ///     Any override name not present in the project config is appended afterward (custom/ad-hoc),
    ///     preserving the order it was supplied in. Replaces any existing entries on the plan.
    /// </summary>
    public static void ApplyProjectVerifications(
        PlanYaml plan, ProjectConfig project, IReadOnlyDictionary<string, VerificationStatus> overrides)
    {
        var seeded = new List<PlanVerificationEntry>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pv in project.Verifications)
        {
            var status = overrides.TryGetValue(pv.Name, out var overrideStatus)
                ? overrideStatus
                : pv.Required ? VerificationStatus.Pending : VerificationStatus.Skipped;
            seeded.Add(new PlanVerificationEntry { Name = pv.Name, Status = status });
            seenNames.Add(pv.Name);
        }

        // Explicit verifications that aren't part of the project config (custom/ad-hoc) are kept,
        // appended after the project set in the order they were supplied.
        foreach (var (name, status) in overrides)
            if (!seenNames.Contains(name))
            {
                seeded.Add(new PlanVerificationEntry { Name = name, Status = status });
                seenNames.Add(name);
            }

        plan.Verifications = seeded;
    }

    /// <summary>
    ///     Orders verifications by their position in the project config (the authoritative run order),
    ///     regardless of how they happen to be stored in plan.yaml. Verifications not present in the
    ///     project config (custom, or since-removed) sort to the end, keeping their relative order.
    ///     Used at the presentation (UI card) and execution (verification list) read points so order
    ///     always follows the current project config even if plan.yaml drifted.
    /// </summary>
    public static List<PlanVerificationEntry> OrderByProjectConfig(
        IEnumerable<PlanVerificationEntry> verifications,
        IReadOnlyList<ProjectVerificationRef>? projectVerifications)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (projectVerifications != null)
            for (var i = 0; i < projectVerifications.Count; i++)
                order[projectVerifications[i].Name] = i;

        // OrderBy is a stable sort, so unknown entries (all int.MaxValue) keep their original order.
        return verifications
            .OrderBy(v => order.TryGetValue(v.Name, out var idx) ? idx : int.MaxValue)
            .ToList();
    }

    /// <summary>
    ///     Reads a plan.yaml file and deserializes it.
    /// </summary>
    public static PlanYaml ReadPlan(string planFolder)
    {
        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException($"plan.yaml not found at {yamlPath}");

        var content = FileHelper.ReadAllText(yamlPath);
        var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(content);
        if (plan == null)
            throw new InvalidOperationException($"Failed to deserialize plan.yaml at {yamlPath}");

        // Initialize null list properties to empty lists
        // This handles cases where YAML has "commits:" with no items or "commits: null"
        plan.Commits ??= new();
        plan.Prs ??= new();
        plan.Repos ??= new();
        plan.Verifications ??= new();
        plan.RelatedPlans ??= new();
        plan.DependsOn ??= new();
        plan.Recommendations ??= new();

        return plan;
    }

    /// <summary>
    ///     Writes a plan.yaml file atomically.
    ///     Validates the plan, writes to temp file, reads back for verification, then atomically moves to target.
    ///     Uses a lock file to coordinate with in-process background writes from PlanReaderService.
    /// </summary>
    public static void WritePlan(string planFolder, PlanYaml plan, IPlanWatcherService? watcher = null)
    {
        // Validate before writing
        PlanValidationService.Validate(plan);

        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        var tempPath = Path.Combine(planFolder, $"plan.yaml.tmp.{Guid.NewGuid():N}");

        using var lockFile = PlanFileLock.Acquire(planFolder);
        try
        {
            // Serialize to temp file
            var yaml = YamlHelper.Serializer.Serialize(plan);
            FileHelper.WriteAllText(tempPath, yaml);

            // Read back and validate
            var content = FileHelper.ReadAllText(tempPath);
            var roundTrip = YamlHelper.Deserializer.Deserialize<PlanYaml>(content);
            if (roundTrip == null)
                throw new InvalidOperationException("Failed to deserialize temp file after writing");

            PlanValidationService.Validate(roundTrip);

            // Atomic move
            File.Move(tempPath, yamlPath, overwrite: true);

            // Notify watcher after successful write
            watcher?.NotifyChanged(Path.GetFileName(planFolder));
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
