using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Review;

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

        var titleRow = Layout.Horizontal().Gap(2).AlignContent(Align.TopLeft)
                       | isChecked.ToBoolInput()
                       | Text.Block(rec.Title).Bold();

        var content = Layout.Vertical().Gap(1) | titleRow;

        if (rec.Impact is { } impact)
            content |= new Badge($"Impact: {impact}").Variant(impact switch
            {
                "High" => BadgeVariant.Success,
                "Medium" => BadgeVariant.Warning,
                _ => BadgeVariant.Outline
            });

        return content
               | new Markdown(MarkdownHelper.PrepareForDisplay(rec.Description, config))
                   .DangerouslyAllowLocalFiles().Article();
    }
}
