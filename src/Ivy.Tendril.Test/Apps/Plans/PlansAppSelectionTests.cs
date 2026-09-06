using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Plans;

public class PlansAppSelectionTests
{
    private static PlanFile CreatePlan(int id, string safeTitle, PlanStatus status = PlanStatus.Draft)
    {
        var folderName = $"{id:D5}-{safeTitle}";
        var folderPath = $"/plans/{folderName}";
        var metadata = new PlanMetadata(
            id,
            "ivy-tendril",
            "Bug",
            safeTitle,
            status,
            [],
            [],
            [],
            [],
            [],
            [],
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null);
        return new PlanFile(metadata, "# " + safeTitle, folderPath, "state: " + status);
    }

    [Fact]
    public void ResolveSelection_WhenNewPlanPrepended_PreservesCurrentlySelectedPlan()
    {
        // Arrange: User is currently viewing plan 37
        var plan38 = CreatePlan(38, "OlderPlan");
        var plan37 = CreatePlan(37, "SelectedPlan");
        var previousPlans = new List<PlanFile> { plan38, plan37 };

        var currentSelected = plan37;
        var savedFolder = plan37.FolderName;

        // Simulate CreatePlan job completing: plan 39 is prepended
        var plan39 = CreatePlan(39, "NewGeneratedPlan");
        var currentPlans = new List<PlanFile> { plan39, plan38, plan37 };

        // Act
        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            currentSelected,
            savedFolder,
            currentPlans,
            previousPlans,
            argPlanId: null);

        // Assert: Selection must remain plan 37, not the newly generated plan 39
        Assert.NotNull(selectedPlan);
        Assert.Equal(37, selectedPlan.Id);
        Assert.Equal(plan37.FolderName, selectedFolder);
    }

    [Fact]
    public void ResolveSelection_WhenSelectedPlanDeletedOrMoved_FallsBackToAdjacentPlan()
    {
        // Arrange: plans list has [40, 39, 38] and user is viewing plan 39 (index 1)
        var plan40 = CreatePlan(40, "Plan40");
        var plan39 = CreatePlan(39, "Plan39");
        var plan38 = CreatePlan(38, "Plan38");
        var previousPlans = new List<PlanFile> { plan40, plan39, plan38 };

        var currentSelected = plan39;
        var savedFolder = plan39.FolderName;

        // Plan 39 is deleted or moved to Executing/Review: list becomes [40, 38]
        var currentPlans = new List<PlanFile> { plan40, plan38 };

        // Act
        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            currentSelected,
            savedFolder,
            currentPlans,
            previousPlans,
            argPlanId: null);

        // Assert: Selection should fall back to adjacent index (index 1 = plan 38)
        Assert.NotNull(selectedPlan);
        Assert.Equal(38, selectedPlan.Id);
        Assert.Equal(plan38.FolderName, selectedFolder);
    }

    [Fact]
    public void ResolveSelection_WhenSelectedPlanAtEndOfListDeleted_FallsBackToLastRemainingPlan()
    {
        // Arrange: user was viewing the last plan (plan 38 at index 2)
        var plan40 = CreatePlan(40, "Plan40");
        var plan39 = CreatePlan(39, "Plan39");
        var plan38 = CreatePlan(38, "Plan38");
        var previousPlans = new List<PlanFile> { plan40, plan39, plan38 };

        var currentSelected = plan38;
        var savedFolder = plan38.FolderName;

        // Plan 38 is deleted: current plans are [40, 39]
        var currentPlans = new List<PlanFile> { plan40, plan39 };

        // Act
        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            currentSelected,
            savedFolder,
            currentPlans,
            previousPlans,
            argPlanId: null);

        // Assert: Nearest valid index is index 1 = plan 39
        Assert.NotNull(selectedPlan);
        Assert.Equal(39, selectedPlan.Id);
        Assert.Equal(plan39.FolderName, selectedFolder);
    }

    [Fact]
    public void ResolveSelection_WhenAllPlansDeleted_ReturnsNull()
    {
        var plan40 = CreatePlan(40, "Plan40");
        var previousPlans = new List<PlanFile> { plan40 };
        var currentPlans = new List<PlanFile>();

        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            plan40,
            plan40.FolderName,
            currentPlans,
            previousPlans,
            argPlanId: null);

        Assert.Null(selectedPlan);
        Assert.Null(selectedFolder);
    }

    [Fact]
    public void ResolveSelection_OnInitialMountWithoutArgs_SelectsFirstPlan()
    {
        var plan40 = CreatePlan(40, "Plan40");
        var plan39 = CreatePlan(39, "Plan39");
        var currentPlans = new List<PlanFile> { plan40, plan39 };
        var previousPlans = new List<PlanFile>();

        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            currentSelected: null,
            savedFolder: null,
            currentPlans,
            previousPlans,
            argPlanId: null);

        Assert.NotNull(selectedPlan);
        Assert.Equal(40, selectedPlan.Id);
        Assert.Equal(plan40.FolderName, selectedFolder);
    }

    [Fact]
    public void ResolveSelection_OnInitialMountWithArgs_SelectsSpecifiedPlan()
    {
        var plan40 = CreatePlan(40, "Plan40");
        var plan39 = CreatePlan(39, "Plan39");
        var currentPlans = new List<PlanFile> { plan40, plan39 };
        var previousPlans = new List<PlanFile>();

        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            currentSelected: null,
            savedFolder: "00039",
            currentPlans,
            previousPlans,
            argPlanId: "00039");

        Assert.NotNull(selectedPlan);
        Assert.Equal(39, selectedPlan.Id);
        Assert.Equal(plan39.FolderName, selectedFolder);
    }

    [Fact]
    public void ResolveSelection_WhenSelectedPlanUpdated_ReturnsFreshInstanceFromList()
    {
        var initialPlan = CreatePlan(39, "Plan39", PlanStatus.Draft);
        var updatedPlan = CreatePlan(39, "Plan39", PlanStatus.Blocked);

        var previousPlans = new List<PlanFile> { initialPlan };
        var currentPlans = new List<PlanFile> { updatedPlan };

        var (selectedPlan, selectedFolder) = PlanSelectionHelper.ResolveSelection(
            initialPlan,
            initialPlan.FolderName,
            currentPlans,
            previousPlans,
            argPlanId: null);

        Assert.NotNull(selectedPlan);
        Assert.Same(updatedPlan, selectedPlan);
        Assert.Equal(PlanStatus.Blocked, selectedPlan.Status);
    }
}
