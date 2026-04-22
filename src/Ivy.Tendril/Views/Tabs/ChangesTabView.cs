using Ivy.Tendril.Helpers;
using Ivy.Widgets.DiffView;

namespace Ivy.Tendril.Views.Tabs;

public class ChangesTabView(
    PlanContentHelpers.AllChangesData? changesData,
    bool loading,
    Exception? error) : ViewBase
{
    public int FileCount => changesData?.Files.Count ?? 0;

    public override object Build()
    {
        if (loading)
            return Text.Muted("Loading...");

        if (changesData is null)
        {
            var errorMsg = error is { } err
                ? $"Failed to load changes: {err.Message}"
                : "No commits yet.";
            return Text.Muted(errorMsg);
        }

        var layout = Layout.Vertical().Gap(4).Padding(2);

        var statsText =
            $"{changesData.Files.Count} files changed ({changesData.AddedCount} added, {changesData.ModifiedCount} modified, {changesData.DeletedCount} deleted)";
        layout |= Text.Block(statsText).Bold();

        if (changesData.Files.Count > 0)
        {
            var filesLayout = Layout.Vertical().Gap(1);
            foreach (var (status, filePath) in changesData.Files)
            {
                var (label, variant) = status switch
                {
                    "A" => ("Added", BadgeVariant.Success),
                    "D" => ("Deleted", BadgeVariant.Destructive),
                    _ => ("Modified", BadgeVariant.Outline)
                };
                filesLayout |= Layout.Horizontal().Gap(2)
                    | new Badge(label).Variant(variant).Small()
                    | Text.Block(filePath);
            }

            layout |= filesLayout;
        }

        if (!string.IsNullOrWhiteSpace(changesData.Diff))
        {
            layout |= new DiffView().Diff(changesData.Diff).Split();
        }

        return layout;
    }
}
