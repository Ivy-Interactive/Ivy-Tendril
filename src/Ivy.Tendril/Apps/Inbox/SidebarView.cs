using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Inbox;

public enum InboxCategory
{
    MyIssues,
    Reviews,
    Project
}

public class SidebarView(
    IState<InboxCategory> selectedCategory,
    IState<string?> selectedProject,
    IReadOnlyList<ProjectConfig> projects,
    int myIssuesCount,
    int reviewsCount,
    IConfigService config,
    Action onSelectMyIssues,
    Action onSelectReviews,
    Action<string> onSelectProject) : ViewBase
{
    public override object Build()
    {
        var isMyIssuesSelected = selectedCategory.Value == InboxCategory.MyIssues;
        var isReviewsSelected = selectedCategory.Value == InboxCategory.Reviews;

        var rows = new List<object>
        {
            SidebarListRow.Build("My issues", Icons.CircleDot, onSelectMyIssues, isMyIssuesSelected, myIssuesCount),
            SidebarListRow.Build("Reviews", Icons.GitPullRequest, onSelectReviews, isReviewsSelected, reviewsCount),
        };

        rows.Add(Text.Label("PROJECTS"));

        if (projects.Count == 0)
        {
            rows.Add(Text.Muted("No projects configured in settings.").Small());
        }
        else
        {
            for (var i = 0; i < projects.Count; i++)
            {
                var proj = projects[i];
                var isSelected = selectedCategory.Value == InboxCategory.Project &&
                                 string.Equals(selectedProject.Value, proj.Name, StringComparison.OrdinalIgnoreCase);
                var projColor = Enum.TryParse<Colors>(proj.Color, out var parsed) ? parsed : (config.GetProjectColor(proj.Name) ?? Colors.Slate);
                rows.Add(SidebarListRow.BuildSubItem(proj.Name, null, projColor, () => onSelectProject(proj.Name), isSelected));
            }
        }

        return Layout.Vertical(rows);
    }
}
