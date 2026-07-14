using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Review;

/// <summary>
///     The project-configured review-actions bar shown above the tabs: one button per
///     <see cref="ReviewActionConfig"/>, disabled when its precomputed condition isn't met,
///     otherwise running the action's PowerShell command in the plan folder. Renders nothing
///     (returns null) when the project defines no review actions, so callers can compose it
///     unconditionally. The action list is unbounded, so buttons are only shown inline at the
///     Wide breakpoint (see <see cref="ActionBarResponsive"/>); at every narrower breakpoint
///     all actions collapse into a single overflow dropdown to avoid clipping.
/// </summary>
public class ReviewActionsBarView(
    PlanFile selectedPlan,
    IReadOnlyList<(string Name, bool ConditionMet)> reviewActionStates,
    IConfigService config,
    ILogger logger) : ViewBase
{
    public override object? Build()
    {
        var projectConfig = config.GetProject(selectedPlan.Project);
        var reviewActions = projectConfig?.ReviewActions ?? [];
        if (reviewActions.Count == 0)
            return null;

        void Run(ReviewActionConfig action)
        {
            if (!PlatformHelper.RunPowerShellAction(action.Command, selectedPlan.FolderPath, logger))
            {
                logger.LogWarning("Failed to run review action {ActionName}: pwsh not found", action.Name);
            }
        }

        // The action list is project-configured and unbounded (arbitrary count, arbitrary
        // label length), so a fixed number of inline buttons can never be guaranteed to fit.
        // Inline buttons are only shown on a wide desktop monitor (>=1024px); at every other
        // breakpoint all actions collapse into a single dropdown so the bar never overflows.
        var actionsBar = Layout.Horizontal().Gap(2).Padding(2, 2, 2, 0).Height(Size.Fit());
        var dropdownItems = new List<MenuItem>();
        for (var i = 0; i < reviewActions.Count; i++)
        {
            var action = reviewActions[i];
            var conditionMet = i < reviewActionStates.Count && reviewActionStates[i].ConditionMet;

            var btn = new Button(action.Name).Icon(Icons.Play).Outline();
            btn = conditionMet ? btn.OnClick(() => Run(action)) : btn.Disabled();
            actionsBar |= btn.WideOnly();

            var menuItem = new MenuItem(action.Name, Icon: Icons.Play, Tag: action.Name).Disabled(!conditionMet);
            if (conditionMet)
            {
                menuItem = menuItem.OnSelect(() => Run(action));
            }
            dropdownItems.Add(menuItem);
        }

        actionsBar |= ActionBarResponsive.DropdownBelowWide(
            new Button().Icon(Icons.EllipsisVertical).Ghost(),
            dropdownItems.ToArray());

        return actionsBar;
    }
}
