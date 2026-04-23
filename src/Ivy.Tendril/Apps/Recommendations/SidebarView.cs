using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Recommendations;

public class SidebarView(
    List<Recommendation> recommendations,
    IState<Recommendation?> selectedState,
    IState<string?> projectFilter,
    IState<string?> impactFilter,
    IState<string?> riskFilter,
    int totalCount,
    bool hasActiveFilters,
    IState<string?> textFilter,
    IState<bool> filtersOpen) : ViewBase
{
    private object BuildHeader()
    {
        var projectOptions = recommendations
            .GroupBy(r => r.Project)
            .OrderByDescending(g => g.Count())
            .Select(g => new Option<string>($"{g.Key} ({g.Count()})", g.Key))
            .ToArray<IAnyOption>();

        var searchInput = textFilter.ToSearchInput()
            .Placeholder("Search...")
            .Suffix(
                new Button()
                    .Icon(filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                    .Ghost()
                    .Small()
                    .OnClick(() => filtersOpen.Set(!filtersOpen.Value))
            );

        var header = Layout.Vertical()
            | (Layout.Vertical().Height(Size.Px(40)).AlignContent(Align.Center) | searchInput);

        if (filtersOpen.Value)
        {
            var impactLevelOptions = new[] { "Small", "Medium", "High" }
                .Select(l => new Option<string>(l, l))
                .ToArray<IAnyOption>();
            var riskLevelOptions = new[] { "Small", "Medium", "High" }
                .Select(l => new Option<string>(l, l))
                .ToArray<IAnyOption>();

            header |= Layout.Vertical()
                | projectFilter.ToSelectInput(projectOptions).Placeholder("All Projects").Nullable()
                    .WithField().Label("Project")
                | impactFilter.ToSelectInput(impactLevelOptions).Placeholder("All Impacts").Nullable()
                    .WithField().Label("Impact")
                | riskFilter.ToSelectInput(riskLevelOptions).Placeholder("All Risk Levels").Nullable()
                    .WithField().Label("Risk");
        }

        return header;
    }

    public override object Build()
    {
        var filtered = recommendations
            .Where(r => projectFilter.Value == null || r.Project == projectFilter.Value)
            .Where(r => impactFilter.Value == null || r.Impact == impactFilter.Value)
            .Where(r => riskFilter.Value == null || r.Risk == riskFilter.Value)
            .Where(r =>
            {
                if (string.IsNullOrWhiteSpace(textFilter.Value)) return true;
                var search = textFilter.Value.ToLowerInvariant();
                return r.Title.ToLowerInvariant().Contains(search) ||
                       r.Description.ToLowerInvariant().Contains(search) ||
                       r.PlanId.Contains(search) ||
                       r.PlanTitle.ToLowerInvariant().Contains(search);
            })
            .ToList();

        if (filtered.Count == 0 && hasActiveFilters && totalCount > 0)
        {
            var emptyContent = Layout.Horizontal().Gap(2).AlignContent(Align.Center).Padding(4)
                   | new Icon(Icons.SearchX).Color(Colors.Gray)
                   | Text.Muted("No results. Try adjusting your filters.");
            return new HeaderLayout(BuildHeader(), emptyContent);
        }

        var content = new List(filtered.Select(rec =>
        {
            var clickableRec = rec;

            var preview = rec.Description.Length > 120
                ? rec.Description[..120] + "..."
                : rec.Description;

            return new ListItem($"#{rec.PlanId} {rec.Title}", preview)
                .OnClick(() => selectedState.Set(clickableRec));
        }));

        return new HeaderLayout(BuildHeader(), content);
    }
}
