using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Recommendations;

public class SidebarView(
    List<Recommendation> recommendations,
    IState<Recommendation?> selectedState,
    IState<string?> projectFilter,
    IState<string?> impactFilter,
    IState<string?> riskFilter,
    int totalCount,
    bool hasActiveFilters,
    IState<string?> textFilter) : ViewBase
{
    private readonly bool _hasActiveFilters = hasActiveFilters;
    private readonly IState<string?> _impactFilter = impactFilter;
    private readonly IState<string?> _projectFilter = projectFilter;
    private readonly List<Recommendation> _recommendations = recommendations;
    private readonly IState<string?> _riskFilter = riskFilter;
    private readonly IState<Recommendation?> _selectedState = selectedState;
    private readonly IState<string?> _textFilter = textFilter;
    private readonly int _totalCount = totalCount;

    private object BuildHeader(IState<bool> filtersOpen)
    {
        var projectOptions = _recommendations
            .GroupBy(r => r.Project)
            .OrderByDescending(g => g.Count())
            .Select(g => new Option<string>($"{g.Key} ({g.Count()})", g.Key))
            .ToArray<IAnyOption>();

        var searchInput = _textFilter.ToSearchInput()
            .Placeholder("Search...")
            .Suffix(
                new Button()
                    .Icon(filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                    .Ghost()
                    .Small()
                    .OnClick(() => filtersOpen.Set(!filtersOpen.Value))
            );

        var header = Layout.Vertical() | searchInput;

        if (filtersOpen.Value)
        {
            var impactLevelOptions = new[] { "Small", "Medium", "High" }
                .Select(l => new Option<string>(l, l))
                .ToArray<IAnyOption>();

            var riskLevelOptions = new[] { "Small", "Medium", "High" }
                .Select(l => new Option<string>(l, l))
                .ToArray<IAnyOption>();

            header |= Layout.Vertical()
                      | _projectFilter.ToSelectInput(projectOptions).Placeholder("All Projects").Nullable()
                          .WithField().Label("Project")
                      | _impactFilter.ToSelectInput(impactLevelOptions).Placeholder("All Impacts").Nullable()
                          .WithField().Label("Impact")
                      | _riskFilter.ToSelectInput(riskLevelOptions).Placeholder("All Risk Levels").Nullable()
                          .WithField().Label("Risk");
        }

        return header;
    }

    private object BuildContent()
    {
        var filtered = _recommendations
            .Where(r => _projectFilter.Value == null || r.Project == _projectFilter.Value)
            .Where(r => _impactFilter.Value == null || r.Impact == _impactFilter.Value)
            .Where(r => _riskFilter.Value == null || r.Risk == _riskFilter.Value)
            .Where(r =>
            {
                if (string.IsNullOrWhiteSpace(_textFilter.Value)) return true;
                var search = _textFilter.Value.ToLowerInvariant();
                return r.Title.ToLowerInvariant().Contains(search) ||
                       r.Description.ToLowerInvariant().Contains(search) ||
                       r.PlanId.Contains(search) ||
                       r.PlanTitle.ToLowerInvariant().Contains(search);
            })
            .ToList();

        if (filtered.Count == 0 && _hasActiveFilters && _totalCount > 0)
            return Layout.Vertical().AlignContent(Align.Center).Gap(2).Padding(4)
                   | new Icon(Icons.ListFilterPlus).Size(Size.Units(6)).Color(Colors.Gray)
                   | Text.Muted("No matching recommendations")
                   | Text.Muted("Try adjusting your filters").Small();

        return new List(filtered.Select(rec =>
        {
            var clickableRec = rec;

            var preview = rec.Description.Length > 120
                ? rec.Description[..120] + "…"
                : rec.Description;

            return new ListItem($"#{rec.PlanId} {rec.Title}", preview)
                .OnClick(() => _selectedState.Set(clickableRec));
        }));
    }

    public override object Build()
    {
        var filtersOpen = UseState(false);

        return new HeaderLayout(BuildHeader(filtersOpen), BuildContent());
    }
}