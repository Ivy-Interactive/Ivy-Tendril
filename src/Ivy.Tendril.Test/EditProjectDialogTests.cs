using Ivy.Tendril.Apps.Settings.Blades;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class EditProjectDialogTests
{
    private static List<VerificationConfig> Defs(params string[] names) =>
        names.Select(n => new VerificationConfig { Name = n }).ToList();

    private static List<ProjectVerificationRef> Project(params string[] names) =>
        names.Select(n => new ProjectVerificationRef { Name = n }).ToList();

    [Fact]
    public void OrderForDisplay_PutsProjectVerificationsFirstInProjectOrder()
    {
        // Global definitions are in a different order than the project's selection.
        var all = Defs("Build", "Test", "Lint", "Format");
        var project = Project("Lint", "Build");

        var ordered = EditProjectBladeView.OrderForDisplay(project, all);

        // Enabled (project) ones first, in project order; then the rest in global order.
        Assert.Equal(new[] { "Lint", "Build", "Test", "Format" }, ordered.Select(v => v.Name));
    }

    [Fact]
    public void OrderForDisplay_NoProjectVerifications_KeepsGlobalOrder()
    {
        var all = Defs("Build", "Test", "Lint");

        var ordered = EditProjectBladeView.OrderForDisplay(Project(), all);

        Assert.Equal(new[] { "Build", "Test", "Lint" }, ordered.Select(v => v.Name));
    }

    [Fact]
    public void OrderForDisplay_IgnoresProjectVerificationsWithoutGlobalDefinition()
    {
        var all = Defs("Build", "Test");
        var project = Project("Stale", "Test");

        var ordered = EditProjectBladeView.OrderForDisplay(project, all);

        Assert.Equal(new[] { "Test", "Build" }, ordered.Select(v => v.Name));
    }

    [Fact]
    public void ReorderProjectVerifications_ReordersEnabledItemsByNewIndices()
    {
        // Displayed: Lint, Build, Test, Format. Project has Lint & Build enabled.
        var displayed = Defs("Lint", "Build", "Test", "Format");
        var project = Project("Lint", "Build");

        // User drags Build above Lint -> new order of displayed indices.
        var newIndices = new[] { 1, 0, 2, 3 };

        var result = EditProjectBladeView.ReorderProjectVerifications(newIndices, displayed, project);

        // Only enabled items, in their new relative order.
        Assert.Equal(new[] { "Build", "Lint" }, result.Select(v => v.Name));
    }

    [Fact]
    public void ReorderProjectVerifications_PreservesRequiredFlags()
    {
        var displayed = Defs("Lint", "Build");
        var project = new List<ProjectVerificationRef>
        {
            new() { Name = "Lint", Required = true },
            new() { Name = "Build", Required = false }
        };

        var result = EditProjectBladeView.ReorderProjectVerifications(new[] { 1, 0 }, displayed, project);

        Assert.Collection(result,
            r => { Assert.Equal("Build", r.Name); Assert.False(r.Required); },
            r => { Assert.Equal("Lint", r.Name); Assert.True(r.Required); });
    }

    [Fact]
    public void ReorderProjectVerifications_DraggingDisabledItemDoesNotAddItToProject()
    {
        // Test is disabled (not in project); dragging it must not enable it.
        var displayed = Defs("Build", "Test", "Lint");
        var project = Project("Build", "Lint");

        // Move the disabled "Test" (index 1) to the front.
        var result = EditProjectBladeView.ReorderProjectVerifications(new[] { 1, 0, 2 }, displayed, project);

        Assert.Equal(new[] { "Build", "Lint" }, result.Select(pv => pv.Name));
        Assert.DoesNotContain(result, pv => pv.Name == "Test");
    }

    [Fact]
    public void ReorderProjectVerifications_KeepsEnabledItemsMissingFromIndices()
    {
        var displayed = Defs("Build", "Test");
        var project = Project("Build", "Test");

        // Indices only reference one displayed item; the other enabled item must survive.
        var result = EditProjectBladeView.ReorderProjectVerifications(new[] { 1 }, displayed, project);

        Assert.Equal(new[] { "Test", "Build" }, result.Select(pv => pv.Name));
    }

    [Fact]
    public void ReorderThenDisplay_RoundTrip_PreservesNewOrderOnReopen()
    {
        // Simulates: reorder via drag -> save to project -> reopen blade.
        var all = Defs("Build", "Test", "Lint");
        var project = Project("Build", "Test", "Lint");

        var displayed = EditProjectBladeView.OrderForDisplay(project, all);
        Assert.Equal(new[] { "Build", "Test", "Lint" }, displayed.Select(v => v.Name));

        // Drag Lint to the top.
        var afterReorder = EditProjectBladeView.ReorderProjectVerifications(new[] { 2, 0, 1 }, displayed, project);
        Assert.Equal(new[] { "Lint", "Build", "Test" }, afterReorder.Select(pv => pv.Name));

        // The Save button persists afterReorder into project.Verifications; reopening loads it back.
        var displayedOnReopen = EditProjectBladeView.OrderForDisplay(afterReorder, all);
        Assert.Equal(new[] { "Lint", "Build", "Test" }, displayedOnReopen.Select(v => v.Name));
    }

    [Fact]
    public void ApplyVerificationChange_TogglingRequired_SetsRequiredFlag()
    {
        var current = new List<ProjectVerificationRef>
        {
            new() { Name = "DotnetTest", Required = false }
        };

        var result = EditProjectBladeView.ApplyVerificationChange(
            """{"name":"DotnetTest","enabled":true,"required":true}""", current);

        Assert.True(result.Single(v => v.Name == "DotnetTest").Required);
    }

    [Fact]
    public void ApplyVerificationChange_EnablingVerification_AddsToProject()
    {
        var result = EditProjectBladeView.ApplyVerificationChange(
            """{"name":"Build","enabled":true,"required":false}""", new List<ProjectVerificationRef>());

        Assert.Contains(result, v => v.Name == "Build");
    }

    [Fact]
    public void ApplyVerificationChange_DisablingVerification_RemovesFromProject()
    {
        var current = Project("Build");

        var result = EditProjectBladeView.ApplyVerificationChange(
            """{"name":"Build","enabled":false,"required":false}""", current);

        Assert.DoesNotContain(result, v => v.Name == "Build");
    }

    [Fact]
    public void ApplyVerificationChange_CamelCaseJson_BindsRequiredTrue()
    {
        var current = Project("Build");

        var result = EditProjectBladeView.ApplyVerificationChange(
            """{"name":"Build","enabled":true,"required":true}""", current);

        Assert.True(result.Single(v => v.Name == "Build").Required);
    }
}
