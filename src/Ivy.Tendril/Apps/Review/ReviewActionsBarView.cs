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
///     unconditionally. The action list is unbounded, so only the first
///     <see cref="MaxInlineAtWide"/> actions are ever shown inline, and only at the Wide
///     breakpoint (see <see cref="ActionBarResponsive"/>); every other action — and
///     everything at every narrower breakpoint — collapses into a single overflow dropdown
///     that stays visible at Wide too, so an arbitrarily long or long-labeled action list
///     can never clip the bar.
/// </summary>
public class ReviewActionsBarView(
    PlanFile selectedPlan,
    IReadOnlyList<(string Name, bool ConditionMet)> reviewActionStates,
    IConfigService config,
    ILogger logger) : ViewBase
{
    private const int MaxInlineAtWide = 4;

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
        // label length), so a fixed number of inline buttons can never be guaranteed to fit
        // even at the Wide breakpoint: cap the inline set at MaxInlineAtWide and always keep
        // every action reachable via the dropdown, which stays visible at Wide too whenever
        // there's overflow. Below Wide, no actions are shown inline at all.
        var actionsBar = Layout.Horizontal().Gap(2).Padding(2, 2, 2, 0).Height(Size.Fit());
        var dropdownItems = new List<MenuItem>();
        var hasOverflowAtWide = reviewActions.Count > MaxInlineAtWide;
        for (var i = 0; i < reviewActions.Count; i++)
        {
            var action = reviewActions[i];
            var conditionMet = i < reviewActionStates.Count && reviewActionStates[i].ConditionMet;

            if (i < MaxInlineAtWide)
            {
                var btn = new Button(action.Name).Icon(Icons.Play).Outline();
                btn = conditionMet ? btn.OnClick(() => Run(action)) : btn.Disabled();
                actionsBar |= btn.FullOnly();
            }

            // Tag carries the loop index so duplicate action names don't misroute clicks or
            // collide as React keys (MenuItem.GetSelectHandler dispatches on the first Tag match).
            var menuItem = new MenuItem(action.Name, Icon: Icons.Play, Tag: $"{i}:{action.Name}")
                .Disabled(!conditionMet);
            if (conditionMet)
            {
                menuItem = menuItem.OnSelect(() => Run(action));
            }
            dropdownItems.Add(menuItem);
        }

        var dropdownTrigger = new Button().Icon(Icons.EllipsisVertical).Ghost();
        actionsBar |= hasOverflowAtWide
            ? dropdownTrigger.WithDropDown(dropdownItems.ToArray())
            : ActionBarResponsive.DropdownBelowWide(dropdownTrigger, dropdownItems.ToArray());

        return actionsBar;
    }
}
