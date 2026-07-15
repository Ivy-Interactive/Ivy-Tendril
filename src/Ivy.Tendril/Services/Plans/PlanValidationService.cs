using System.Globalization;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Centralized validation for plan.yaml files.
///     Reuses logic from DoctorCommand.CheckYamlHealth() but provides detailed error messages.
/// </summary>
public static class PlanValidationService
{
    private static readonly string[] ValidStates =
    [
        nameof(PlanStatus.Draft), nameof(PlanStatus.Creating), nameof(PlanStatus.Updating), nameof(PlanStatus.Executing), nameof(PlanStatus.Review),
        nameof(PlanStatus.Failed), nameof(PlanStatus.Completed), nameof(PlanStatus.Skipped), nameof(PlanStatus.Blocked), nameof(PlanStatus.Icebox)
    ];

    /// <summary>
    ///     Validates a PlanYaml object. Throws ArgumentException with detailed error message on failure.
    ///     When <paramref name="project" /> is supplied, also enforces that every repo belongs to it
    ///     (issue #1340) — callers that have config resolved should pass it.
    /// </summary>
    public static void Validate(PlanYaml plan, string[]? configuredLevels = null, ProjectConfig? project = null)
    {
        // Required fields
        if (string.IsNullOrWhiteSpace(plan.State))
            throw new ArgumentException("Required field 'state' is missing or empty");

        if (string.IsNullOrWhiteSpace(plan.Project))
            throw new ArgumentException("Required field 'project' is missing or empty");

        if (string.IsNullOrWhiteSpace(plan.Title))
            throw new ArgumentException("Required field 'title' is missing or empty");

        // Validate state enum
        if (!ValidStates.Contains(plan.State, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid state value '{plan.State}'. Valid states: {string.Join(", ", ValidStates)}");

        // Validate level against configured levels (if provided)
        if (configuredLevels is { Length: > 0 } &&
            !configuredLevels.Contains(plan.Level, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid level value '{plan.Level}'. Valid levels: {string.Join(", ", configuredLevels)}");

        // Validate dates
        ValidateDate(plan.Created, "created");
        ValidateDate(plan.Updated, "updated");

        // Validate repos (unless Completed with PRs/commits)
        if (plan.Repos == null || plan.Repos.Count == 0)
        {
            var isCompleted = plan.State.Equals(nameof(PlanStatus.Completed), StringComparison.OrdinalIgnoreCase);
            var hasPrsOrCommits = (plan.Prs?.Count > 0) || (plan.Commits?.Count > 0);
            if (!isCompleted || !hasPrsOrCommits)
                throw new ArgumentException("Field 'repos' is empty. At least one repository is required.");
        }

        // Validate repo paths exist
        if (plan.Repos != null)
        {
            foreach (var repo in plan.Repos)
            {
                if (!Directory.Exists(repo))
                    throw new ArgumentException($"Repository path '{repo}' does not exist");
            }
        }

        // Validate PR URLs format
        if (plan.Prs != null)
        {
            foreach (var pr in plan.Prs)
            {
                if (!Uri.TryCreate(pr, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    throw new ArgumentException($"Invalid PR URL format: {pr}");
            }
        }

        // Validate commit hashes (basic format check)
        if (plan.Commits != null)
        {
            foreach (var commit in plan.Commits)
            {
                if (string.IsNullOrWhiteSpace(commit) || commit.Length < 7 || commit.Length > 40)
                    throw new ArgumentException(
                        $"Invalid commit hash format: {commit}. Expected 7-40 character hex string.");

                if (!commit.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    throw new ArgumentException($"Invalid commit hash format: {commit}. Must be hexadecimal.");
            }
        }

        // Validate verifications
        if (plan.Verifications != null)
        {
            foreach (var verification in plan.Verifications)
            {
                if (string.IsNullOrWhiteSpace(verification.Name))
                    throw new ArgumentException("Verification entry has empty name");

                // Status is a VerificationStatus enum — always valid once deserialized.
            }
        }

        // Validate repos belong to the project (issue #1340), when the project is known.
        if (project != null && plan.Repos != null)
            PlanProjectRepoGuard.EnsureReposBelongToProject(plan.Repos, project);
    }

    private static void ValidateDate(DateTime date, string fieldName)
    {
        // Check if date is within reasonable range
        if (date < new DateTime(2020, 1, 1) || date > DateTime.UtcNow.AddYears(1))
            throw new ArgumentException(
                $"Invalid date for '{fieldName}': {date:O}. Date must be between 2020-01-01 and one year from now.");
    }

    /// <summary>
    ///     Parses a date string in ISO 8601 format. Throws ArgumentException on failure.
    /// </summary>
    public static DateTime ParseDate(string dateString, string fieldName)
    {
        if (!DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            throw new ArgumentException(
                $"Invalid date format for '{fieldName}'. Expected ISO 8601 (e.g., 2026-04-18T15:29:55Z)");

        return date;
    }
}
