using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Review;

/// <summary>
///     The project-configured review-actions bar shown above the tabs: one button per
///     <see cref="ReviewActionConfig"/>, disabled when its precomputed condition isn't met,
///     otherwise opening a <see cref="ReviewActionApp"/> tab that runs the action's PowerShell
///     command in a PTY. Renders nothing (returns null) when the project defines no review
///     actions, so callers can compose it unconditionally.
/// </summary>
public class ReviewActionsBarView(
    PlanFile selectedPlan,
    IReadOnlyList<(string Name, bool ConditionMet)> reviewActionStates,
    IConfigService config) : ViewBase
{
    public override object? Build()
    {
        var nav = UseNavigation();

        var projectConfig = config.GetProject(selectedPlan.Project);
        var reviewActions = projectConfig?.ReviewActions ?? [];
        if (reviewActions.Count == 0)
            return null;

        var actionsBar = Layout.Horizontal().Gap(2).Padding(2, 2, 1, 2).Height(Size.Fit());
        for (var i = 0; i < reviewActions.Count; i++)
        {
            var action = reviewActions[i];
            var conditionMet = i < reviewActionStates.Count && reviewActionStates[i].ConditionMet;
            var actionCapture = action;

            var btn = BuildActionButton(
                action,
                conditionMet,
                () => nav.Navigate<ReviewActionApp>(new ReviewActionAppArgs(selectedPlan.FolderName, actionCapture.Name)));

            actionsBar |= btn;
        }

        return actionsBar;
    }

    internal static string GetTooltip(ReviewActionConfig action, bool conditionMet)
    {
        if (!conditionMet)
        {
            return !string.IsNullOrWhiteSpace(action.Condition)
                ? $"Disabled: Condition not met ({action.Condition})"
                : "Disabled: Condition not met";
        }

        return !string.IsNullOrWhiteSpace(action.Command)
            ? $"Run: {action.Command}"
            : $"Run {action.Name}";
    }

    internal static Button BuildActionButton(ReviewActionConfig action, bool conditionMet, Action? onNavigate = null)
    {
        var btn = new Button(action.Name).Icon(Icons.Play).Outline();
        var tooltip = GetTooltip(action, conditionMet);

        if (!conditionMet)
        {
            return btn.Disabled().Tooltip(tooltip);
        }

        btn = btn.Tooltip(tooltip);
        if (onNavigate != null)
        {
            btn = btn.OnClick(onNavigate);
        }

        return btn;
    }
}
