using Ivy.Tendril.Apps.Recommendations;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

[App(title: "Recommendations", icon: Icons.Lightbulb, group: ["Apps"], order: MenuOrder.Recommendations)]
public class RecommendationsApp : ViewBase
{
    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var refreshToken = UseRefreshToken();
        var selectedState = UseState<Recommendation?>(null);
        var projectFilter = UseState<string?>(null);
        var impactFilter = UseState<string?>(null);
        var riskFilter = UseState<string?>(null);
        var textFilter = UseState<string?>("");

        UseInterval(() => refreshToken.Refresh(), TimeSpan.FromMinutes(1));
        var filtersOpen = UseState(false);

        var recommendations = planService.GetRecommendations();

        var allPending = recommendations.Where(r => r.State == "Pending").ToList();

        var filtered = allPending
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

        if (selectedState.Value == null && filtered.Count > 0) selectedState.Set(filtered[0]);

        // If selected recommendation is no longer in filtered list, adjust selection
        if (selectedState.Value is { } selected &&
            !filtered.Any(r => r.PlanId == selected.PlanId && r.Title == selected.Title))
            selectedState.Set(filtered.Count > 0 ? filtered[0] : null);

        void Refresh()
        {
            refreshToken.Refresh();
        }

        var totalPendingCount = allPending.Count;
        var hasActiveFilters = projectFilter.Value != null ||
                               impactFilter.Value != null || riskFilter.Value != null ||
                               !string.IsNullOrWhiteSpace(textFilter.Value);

        var projectOptions = allPending
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
        var sidebarHeader = Layout.Vertical() | searchInput;
        if (filtersOpen.Value)
        {
            var impactLevelOptions = new[] { "Small", "Medium", "High" }
                .Select(l => new Option<string>(l, l))
                .ToArray<IAnyOption>();
            var riskLevelOptions = new[] { "Small", "Medium", "High" }
                .Select(l => new Option<string>(l, l))
                .ToArray<IAnyOption>();

            sidebarHeader |= Layout.Vertical()
                | projectFilter.ToSelectInput(projectOptions).Placeholder("All Projects").Nullable()
                    .WithField().Label("Project")
                | impactFilter.ToSelectInput(impactLevelOptions).Placeholder("All Impacts").Nullable()
                    .WithField().Label("Impact")
                | riskFilter.ToSelectInput(riskLevelOptions).Placeholder("All Risk Levels").Nullable()
                    .WithField().Label("Risk");
        }

        var sidebar = new SidebarView(
            allPending,
            selectedState,
            projectFilter,
            impactFilter,
            riskFilter,
            totalPendingCount,
            hasActiveFilters,
            textFilter
        );

        return new SidebarLayout(
            new ContentView(selectedState.Value, filtered, selectedState, planService, jobService, Refresh),
            sidebar,
            sidebarHeader: sidebarHeader
        );
    }
}
