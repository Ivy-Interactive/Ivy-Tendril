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
///     unconditionally.
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

        var actionsBar = Layout.Horizontal().Gap(2).Padding(2, 2, 1, 2).Height(Size.Fit());
        for (var i = 0; i < reviewActions.Count; i++)
        {
            var action = reviewActions[i];
            var conditionMet = i < reviewActionStates.Count && reviewActionStates[i].ConditionMet;

            var btn = new Button(action.Name).Icon(Icons.Play).Outline();
            if (!conditionMet)
            {
                btn = btn.Disabled();
            }
            else
            {
                var actionCapture = action;
                btn = btn.OnClick(() =>
                {
                    if (!PlatformHelper.RunPowerShellAction(actionCapture.Command, selectedPlan.FolderPath, logger))
                    {
                        logger.LogWarning("Failed to run review action {ActionName}: pwsh not found", actionCapture.Name);
                    }
                });
            }

            actionsBar |= btn;
        }

        return actionsBar;
    }
}
