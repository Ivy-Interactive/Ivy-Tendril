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
                () => nav.Navigate<ReviewActionApp>(new ReviewActionAppArgs(selectedPlan.FolderName, actionCapture.Name)),
                selectedPlan.AllocatedPorts);

            actionsBar |= btn;
        }

        return actionsBar;
    }

    /// <summary>
    ///     The button's tooltip. When the plan has ports allocated they are appended to the command, so a
    ///     reviewer can see which URLs the action will serve on without opening the terminal first.
    /// </summary>
    internal static string GetTooltip(
        ReviewActionConfig action, bool conditionMet, IReadOnlyDictionary<string, int>? allocatedPorts = null)
    {
        if (!conditionMet)
        {
            return !string.IsNullOrWhiteSpace(action.Condition)
                ? $"Disabled: Condition not met ({action.Condition})"
                : "Disabled: Condition not met";
        }

        var ports = allocatedPorts is { Count: > 0 }
            ? $" (ports: {string.Join(", ", allocatedPorts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}: {p.Value}"))})"
            : "";

        return !string.IsNullOrWhiteSpace(action.Command)
            ? $"Run: {action.Command}{ports}"
            : $"Run {action.Name}{ports}";
    }

    internal static Button BuildActionButton(
        ReviewActionConfig action, bool conditionMet, Action? onNavigate = null,
        IReadOnlyDictionary<string, int>? allocatedPorts = null)
    {
        var btn = new Button(action.Name).Icon(Icons.Play).Outline();
        var tooltip = GetTooltip(action, conditionMet, allocatedPorts);

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
