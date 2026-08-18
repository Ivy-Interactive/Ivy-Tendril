using System.Diagnostics.CodeAnalysis;
using Ivy.Tendril.Models;
using SpectreValidation = Spectre.Console.ValidationResult;

namespace Ivy.Tendril.Helpers;

public static class CliValidation
{
    public static readonly string[] ValidStates =
    [
        nameof(PlanStatus.Draft), nameof(PlanStatus.Creating), nameof(PlanStatus.Updating),
        nameof(PlanStatus.Executing), nameof(PlanStatus.Review), nameof(PlanStatus.Failed),
        nameof(PlanStatus.Completed), nameof(PlanStatus.Skipped), nameof(PlanStatus.Blocked),
        nameof(PlanStatus.Icebox)
    ];

    public static readonly string[] ValidLevels =
    [
        "Bug", "Feature", "Epic", "Chore", "Nitpick"
    ];

    public static readonly string[] ValidVerificationStatuses =
    [
        nameof(VerificationStatus.Pending), nameof(VerificationStatus.Pass),
        nameof(VerificationStatus.Fail), nameof(VerificationStatus.Skipped)
    ];

    public static readonly string[] ValidRecommendationStates =
    [
        RecommendationStatus.Pending, RecommendationStatus.Accepted,
        RecommendationStatus.AcceptedWithNotes, RecommendationStatus.Declined
    ];

    public static readonly string[] ValidFormats = ["table", "ids", "folders", "json"];

    public static readonly string[] ValidJobStatuses =
    [
        nameof(JobStatus.Pending), nameof(JobStatus.Queued), nameof(JobStatus.Running),
        nameof(JobStatus.Completed), nameof(JobStatus.Failed), nameof(JobStatus.Timeout),
        nameof(JobStatus.Stopped), nameof(JobStatus.Blocked)
    ];

    public static readonly string[] ValidExecutionProfiles = ["deep", "balanced"];

    public static readonly string[] ValidImpactLevels = ["Small", "Medium", "High"];

    public static SpectreValidation RequireNonEmpty(string? value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SpectreValidation.Error($"<{argumentName}> is required and cannot be empty.");
        return SpectreValidation.Success();
    }

    public static SpectreValidation ValidateOneOf(string? value, string optionName, string[] allowed)
    {
        if (string.IsNullOrEmpty(value))
            return SpectreValidation.Success();

        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            return SpectreValidation.Error(
                $"Invalid value '{value}' for {optionName}. Valid values: {string.Join(", ", allowed)}");

        return SpectreValidation.Success();
    }

    public static SpectreValidation ValidateProjectName(string? name)
    {
        var error = InputSanitizer.DescribeProjectNameError(name);
        return error is null ? SpectreValidation.Success() : SpectreValidation.Error(error);
    }

    public static SpectreValidation ValidateField(string? value, string[] validFields)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SpectreValidation.Error(
                $"<field> is required and cannot be empty. Valid fields: {string.Join(", ", validFields)}");

        if (!validFields.Contains(value, StringComparer.OrdinalIgnoreCase))
            return SpectreValidation.Error(
                $"Unknown field '{value}'. Valid fields: {string.Join(", ", validFields)}");

        return SpectreValidation.Success();
    }

    public static SpectreValidation Combine(params SpectreValidation[] results)
    {
        foreach (var r in results)
            if (!r.Successful)
                return r;
        return SpectreValidation.Success();
    }

    [DoesNotReturn]
    private static void ThrowNotFound(string entity, string name, IEnumerable<string> available) =>
        throw new InvalidOperationException(
            $"{entity} not found: '{name}'. Available: {string.Join(", ", available)}");

    [DoesNotReturn]
    public static void ThrowVerificationNotFound(string name, IEnumerable<string> available) =>
        ThrowNotFound("Verification", name, available);

    [DoesNotReturn]
    public static void ThrowProjectNotFound(string name, IEnumerable<string> available) =>
        ThrowNotFound("Project", name, available);

    [DoesNotReturn]
    public static void ThrowRepoNotFound(string path, IEnumerable<string> available) =>
        ThrowNotFound("Repository", path, available);

    [DoesNotReturn]
    public static void ThrowReviewActionNotFound(string name, IEnumerable<string> available) =>
        ThrowNotFound("Review action", name, available);

    [DoesNotReturn]
    public static void ThrowBuildDependencyNotFound(string name, IEnumerable<string> available) =>
        ThrowNotFound("Build dependency", name, available);

    [DoesNotReturn]
    public static void ThrowVerificationTargetNotFound(string optionName, string name, IEnumerable<string> available) =>
        ThrowNotFound($"Target verification for --{optionName}", name, available);

    /// <summary>Number of value sources supplied (inline arg / --file / --stdin), for the "exactly one" rule.</summary>
    public static int CountSources(bool stdin, string? filePath, string value) =>
        (stdin ? 1 : 0) + (!string.IsNullOrEmpty(filePath) ? 1 : 0) + (!string.IsNullOrEmpty(value) ? 1 : 0);

    // Shared "provide long-form input exactly one way" rule.
    // inlineSources names the allowed sources, e.g.
    // "an inline <value>, --file, or --stdin" or "--prompt, --file, or --stdin".
    public static SpectreValidation ValidateSingleSource(int sourceCount, string inlineSources) =>
        sourceCount > 1
            ? SpectreValidation.Error($"Provide the value in exactly one way: {inlineSources}.")
            : SpectreValidation.Success();

    public static void ValidateConfiguredProject(string projectName, IEnumerable<string> availableProjects)
    {
        if (!availableProjects.Contains(projectName, StringComparer.OrdinalIgnoreCase))
        {
            ThrowProjectNotFound(projectName, availableProjects);
        }
    }
}
