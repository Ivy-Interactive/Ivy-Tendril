using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Review.Tabs;

/// <summary>
///     The "Recommendations" tab in the Review app: an optional "Implement Recommendations"
///     button (shown once at least one row is checked, badged with the count) over the list of
///     pending recommendation rows. The button raises <paramref name="onImplement"/>; the actual
///     job-starting logic stays in ContentView. Each row owns its own checkbox via
///     <see cref="RecommendationRowView"/>.
/// </summary>
public class RecommendationsTabView(
    List<RecommendationYaml> pendingRecs,
    IState<HashSet<string>> selectedRecTitles,
    IConfigService config,
    Action onImplement) : ViewBase
{
    public override object Build()
    {
        var layout = Layout.Vertical().Padding(2);

        if (selectedRecTitles.Value.Count > 0)
        {
            var count = selectedRecTitles.Value.Count;
            layout |= Layout.Horizontal().Gap(2).AlignContent(Align.Right)
                | new Button("Implement Recommendations")
                    .Icon(Icons.Rocket).Badge(count.ToString()).Primary()
                    .OnClick(onImplement);
        }

        if (pendingRecs.Count == 0)
        {
            layout |= Text.Muted("No recommendations.");
        }
        else
        {
            for (var i = 0; i < pendingRecs.Count; i++)
            {
                layout |= new RecommendationRowView(pendingRecs[i], selectedRecTitles, config);
                if (i < pendingRecs.Count - 1)
                    layout |= new Separator();
            }
        }

        return layout;
    }
}

/// <summary>
///     A single recommendation row: a checkbox bound to the shared selection set, plus title,
///     optional impact badge, and description. Kept as its own view so each row owns its checkbox
///     state (no hooks in a loop) — mirrors the VerificationRowView pattern.
/// </summary>
public class RecommendationRowView(
    RecommendationYaml rec,
    IState<HashSet<string>> selectedTitles,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(() => selectedTitles.Value.Contains(rec.Title));
        var previous = UseState(isChecked.Value);

        UseEffect(() =>
        {
            if (isChecked.Value == previous.Value) return;
            previous.Set(isChecked.Value);
            var set = new HashSet<string>(selectedTitles.Value);
            if (isChecked.Value) set.Add(rec.Title); else set.Remove(rec.Title);
            selectedTitles.Set(set);
        }, isChecked);

        object? badge = null;

        if (rec.Impact is { } impact)
        {
            badge = new Badge(impact).Variant(impact switch
            {
                "High" => BadgeVariant.Success,
                "Medium" => BadgeVariant.Warning,
                _ => BadgeVariant.Outline
            });
        }

        var titleRow = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                       | isChecked.ToBoolInput()
                       | badge
                       | Text.Block(rec.Title).Bold();

        var content = Layout.Vertical().Gap(1) | titleRow;



        return content
               | new Markdown(MarkdownHelper.PrepareForDisplay(rec.Description, config))
                   .DangerouslyAllowLocalFiles()
                   .Article();
    }
}
