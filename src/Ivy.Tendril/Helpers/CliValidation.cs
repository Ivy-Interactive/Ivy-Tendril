using Ivy.Tendril.Models;
using SpectreValidation = Spectre.Console.ValidationResult;

namespace Ivy.Tendril.Helpers;

public static class CliValidation
{
    public static readonly string[] ValidStates =
    [
        nameof(PlanStatus.Draft), nameof(PlanStatus.Building), nameof(PlanStatus.Updating),
        nameof(PlanStatus.Executing), nameof(PlanStatus.ReadyForReview), nameof(PlanStatus.Failed),
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

    public static readonly string[] ValidExecutionProfiles = ["deep", "balanced"];

    public static readonly string[] ValidImpactLevels = ["Small", "Medium", "High"];

    public static readonly string[] ValidPrRules = ["default", "yolo"];

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
}
