using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class ApplyProjectVerificationsTests
{
    private static ProjectConfig Project() => new()
    {
        Name = "TestProject",
        Verifications =
        [
            new ProjectVerificationRef { Name = "Build", Required = true },
            new ProjectVerificationRef { Name = "Format", Required = true },
            new ProjectVerificationRef { Name = "Lint", Required = false },
            new ProjectVerificationRef { Name = "E2E", Required = false }
        ]
    };

    private static Dictionary<string, VerificationStatus> NoOverrides() => new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NoOverrides_SeedsFullSet_RequiredPending_OptionalSkipped_InProjectOrder()
    {
        var plan = new PlanYaml();

        PlanCommandHelpers.ApplyProjectVerifications(plan, Project(), NoOverrides());

        Assert.Equal(new[] { "Build", "Format", "Lint", "E2E" },
            plan.Verifications.Select(v => v.Name).ToArray());
        Assert.Equal(VerificationStatus.Pending, plan.Verifications[0].Status); // Build (required)
        Assert.Equal(VerificationStatus.Pending, plan.Verifications[1].Status); // Format (required)
        Assert.Equal(VerificationStatus.Skipped, plan.Verifications[2].Status); // Lint (optional)
        Assert.Equal(VerificationStatus.Skipped, plan.Verifications[3].Status); // E2E (optional)
    }

    [Fact]
    public void Overrides_Win_IncludingSkippingARequiredAndEnablingAnOptional()
    {
        var plan = new PlanYaml();
        var overrides = new Dictionary<string, VerificationStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["Build"] = VerificationStatus.Skipped, // agent explicitly skips a required one
            ["Lint"] = VerificationStatus.Pending   // agent enables an optional one
        };

        PlanCommandHelpers.ApplyProjectVerifications(plan, Project(), overrides);

        Assert.Equal(VerificationStatus.Skipped, plan.Verifications.First(v => v.Name == "Build").Status);
        Assert.Equal(VerificationStatus.Pending, plan.Verifications.First(v => v.Name == "Format").Status); // default
        Assert.Equal(VerificationStatus.Pending, plan.Verifications.First(v => v.Name == "Lint").Status);
        Assert.Equal(VerificationStatus.Skipped, plan.Verifications.First(v => v.Name == "E2E").Status); // default
    }

    [Fact]
    public void OverrideNotInProjectConfig_IsAppendedAfterProjectSet()
    {
        var plan = new PlanYaml();
        var overrides = new Dictionary<string, VerificationStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomCheck"] = VerificationStatus.Pending
        };

        PlanCommandHelpers.ApplyProjectVerifications(plan, Project(), overrides);

        Assert.Equal(5, plan.Verifications.Count);
        Assert.Equal("CustomCheck", plan.Verifications[^1].Name);
        Assert.Equal(VerificationStatus.Pending, plan.Verifications[^1].Status);
    }

    [Fact]
    public void ReplacesAnyExistingEntries()
    {
        var plan = new PlanYaml
        {
            Verifications = [new PlanVerificationEntry { Name = "Stale", Status = VerificationStatus.Pass }]
        };

        PlanCommandHelpers.ApplyProjectVerifications(plan, Project(), NoOverrides());

        Assert.DoesNotContain(plan.Verifications, v => v.Name == "Stale");
        Assert.Equal(4, plan.Verifications.Count);
    }

    [Fact]
    public void EmptyProject_SeedsNothing()
    {
        var plan = new PlanYaml();
        var emptyProject = new ProjectConfig { Name = "Auto" };

        PlanCommandHelpers.ApplyProjectVerifications(plan, emptyProject, NoOverrides());

        Assert.Empty(plan.Verifications);
    }

    // --- OrderByProjectConfig ---

    [Fact]
    public void OrderByProjectConfig_ReordersStoredEntriesToProjectOrder()
    {
        // Stored out of order (e.g. plan.yaml drifted)
        var stored = new List<PlanVerificationEntry>
        {
            new() { Name = "E2E", Status = VerificationStatus.Pending },
            new() { Name = "Build", Status = VerificationStatus.Pending },
            new() { Name = "Lint", Status = VerificationStatus.Skipped },
            new() { Name = "Format", Status = VerificationStatus.Pending }
        };

        var ordered = PlanCommandHelpers.OrderByProjectConfig(stored, Project().Verifications);

        Assert.Equal(new[] { "Build", "Format", "Lint", "E2E" }, ordered.Select(v => v.Name).ToArray());
    }

    [Fact]
    public void OrderByProjectConfig_UnknownEntries_SortToEnd_StablyKeepingRelativeOrder()
    {
        var stored = new List<PlanVerificationEntry>
        {
            new() { Name = "Custom1", Status = VerificationStatus.Pending },
            new() { Name = "Lint", Status = VerificationStatus.Skipped },
            new() { Name = "Custom2", Status = VerificationStatus.Pending },
            new() { Name = "Build", Status = VerificationStatus.Pending }
        };

        var ordered = PlanCommandHelpers.OrderByProjectConfig(stored, Project().Verifications);

        // Known ones first in config order, then unknowns in original relative order.
        Assert.Equal(new[] { "Build", "Lint", "Custom1", "Custom2" }, ordered.Select(v => v.Name).ToArray());
    }

    [Fact]
    public void OrderByProjectConfig_NullProjectConfig_PreservesOriginalOrder()
    {
        var stored = new List<PlanVerificationEntry>
        {
            new() { Name = "B", Status = VerificationStatus.Pending },
            new() { Name = "A", Status = VerificationStatus.Pending }
        };

        var ordered = PlanCommandHelpers.OrderByProjectConfig(stored, null);

        Assert.Equal(new[] { "B", "A" }, ordered.Select(v => v.Name).ToArray());
    }
}
