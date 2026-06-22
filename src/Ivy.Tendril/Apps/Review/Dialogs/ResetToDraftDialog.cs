using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class ResetToDraftDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IPlanReaderService planService,
    Action refreshPlans,
    ILogger<ResetToDraftDialog> logger) : ViewBase
{
    private readonly IState<bool> _dialogOpen = dialogOpen;
    private readonly IPlanReaderService _planService = planService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile _selectedPlan = selectedPlan;
    private readonly ILogger<ResetToDraftDialog> _logger = logger;

    public override object? Build()
    {
        var isResetting = UseState(false);

        if (!_dialogOpen.Value) return null;

        return new Dialog(
            _ =>
            {
                isResetting.Set(false);
                _dialogOpen.Set(false);
            },
            new DialogHeader($"Reset Plan #{_selectedPlan.Id} to Draft"),
            new DialogBody(
                Text.P("Are you sure you want to reset this plan? Will remove all worktrees and artifacts.")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => _dialogOpen.Set(false)),
                new Button("Reset to Draft").Warning().Disabled(isResetting.Value).ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    if (!isResetting.Value)
                    {
                        isResetting.Set(true);

                        _planService.ResetToDraft(_selectedPlan.FolderName);
                        _refreshPlans();
                        _dialogOpen.Set(false);

                        var folderPath = _selectedPlan.FolderPath;
                        Task.Run(() =>
                        {
                            try
                            {
                                WorktreeCleanupService.CleanPlanState(folderPath, _logger);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to clean plan state for {FolderPath}", folderPath);
                            }
                        });
                    }
                })
            )
        ).Width(Size.Rem(40));
    }
}
