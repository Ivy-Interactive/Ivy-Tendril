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
///     unconditionally. All actions render inline at the Wide breakpoint on a wrapping row
///     (an arbitrarily long list flows onto extra rows instead of clipping); at narrower
///     breakpoints everything collapses into a single dropdown
///     (see <see cref="ActionBarResponsive"/>).
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

        var actionsBar = Layout.Horizontal().Gap(2).Padding(2, 2, 2, 0).Height(Size.Fit()).Wrap();
        var dropdownItems = new List<MenuItem>();
        for (var i = 0; i < reviewActions.Count; i++)
        {
            var action = reviewActions[i];
            var conditionMet = i < reviewActionStates.Count && reviewActionStates[i].ConditionMet;

            var btn = new Button(action.Name).Icon(Icons.Play).Outline();
            btn = conditionMet ? btn.OnClick(() => Run(action)) : btn.Disabled();
            actionsBar |= btn.FullOnly();

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

        actionsBar |= ActionBarResponsive.DropdownBelowWide(
            new Button().Icon(Icons.EllipsisVertical).Ghost(),
            dropdownItems.ToArray());

        return actionsBar;
    }
}
