using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Views;

public static class PlanNeighborShortcuts
{
    public const string PreviousTag = "PreviousPlan";
    public const string NextTag = "NextPlan";

    public static PlanWorkspaceActions Add(PlanWorkspaceActions actions, IReadOnlyList<PlanFile> plans, int currentIndex, Action<PlanFile> select)
    {
        if (currentIndex > 0)
            actions.Shortcut(PreviousTag, "Previous plan", "ArrowLeft", () => select(plans[currentIndex - 1]));
        if (currentIndex >= 0 && currentIndex < plans.Count - 1)
            actions.Shortcut(NextTag, "Next plan", "ArrowRight", () => select(plans[currentIndex + 1]));
        return actions;
    }
}
