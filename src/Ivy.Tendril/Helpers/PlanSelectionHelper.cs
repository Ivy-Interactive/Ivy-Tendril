using Ivy.Tendril.Models;

namespace Ivy.Tendril.Helpers;

public static class PlanSelectionHelper
{
    public static (PlanFile? SelectedPlan, string? SelectedFolder) ResolveSelection(
        PlanFile? currentSelected,
        string? savedFolder,
        IReadOnlyList<PlanFile> currentPlans,
        IReadOnlyList<PlanFile> previousPlans,
        string? argPlanId)
    {
        if (currentSelected is { } selected)
        {
            var matchingPlan = currentPlans.FirstOrDefault(p =>
                p.FolderName.Equals(selected.FolderName, StringComparison.OrdinalIgnoreCase) ||
                p.Id == selected.Id);

            if (matchingPlan != null)
            {
                return (matchingPlan, matchingPlan.FolderName);
            }

            var oldIndex = previousPlans.ToList().FindIndex(p =>
                p.FolderName.Equals(selected.FolderName, StringComparison.OrdinalIgnoreCase) ||
                p.Id == selected.Id);

            if (currentPlans.Count > 0)
            {
                var newIndex = oldIndex >= 0 ? Math.Min(oldIndex, currentPlans.Count - 1) : 0;
                var fallback = currentPlans[newIndex];
                return (fallback, fallback.FolderName);
            }

            return (null, null);
        }

        if (!string.IsNullOrEmpty(savedFolder))
        {
            var matchedSaved = currentPlans.FirstOrDefault(p =>
                p.FolderName.Equals(savedFolder, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString() == savedFolder ||
                p.FolderName.StartsWith(savedFolder + "-", StringComparison.OrdinalIgnoreCase));

            if (matchedSaved != null)
            {
                return (matchedSaved, matchedSaved.FolderName);
            }

            var oldIndex = previousPlans.ToList().FindIndex(p =>
                p.FolderName.Equals(savedFolder, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString() == savedFolder ||
                p.FolderName.StartsWith(savedFolder + "-", StringComparison.OrdinalIgnoreCase));

            if (currentPlans.Count > 0)
            {
                var newIndex = oldIndex >= 0 ? Math.Min(oldIndex, currentPlans.Count - 1) : 0;
                var fallback = currentPlans[newIndex];
                return (fallback, fallback.FolderName);
            }

            return (null, null);
        }

        if (currentSelected == null && currentPlans.Count > 0 && string.IsNullOrEmpty(argPlanId))
        {
            return (currentPlans[0], currentPlans[0].FolderName);
        }

        return (currentSelected, savedFolder);
    }
}
