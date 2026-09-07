using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Inbox;

namespace Ivy.Tendril.Apps.Inbox.Dialogs;

public class AutoAcceptSettingsDialog(
    IState<bool> isOpen,
    IConfigService config,
    AssignedIssuesAutoImportService? autoImportService,
    RefreshToken refreshToken,
    Func<Task> onRefresh) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        var autoAccept = UseState(config.Settings.Inbox.AutoAcceptAssignedIssues);
        var checkInterval = UseState(config.Settings.Inbox.CheckIntervalMinutes);
        var isCheckingNow = UseState(false);

        if (!isOpen.Value) return null;

        var intervalOptions = new[]
        {
            new Option<int>("5 minutes", 5),
            new Option<int>("10 minutes", 10),
            new Option<int>("15 minutes", 15),
            new Option<int>("30 minutes", 30),
            new Option<int>("60 minutes", 60),
        };

        var dialogBody = Layout.Vertical().Gap(4)
            | Text.P("Automatically import newly assigned GitHub issues into Tendril plans at regular intervals.").Muted().Small()
            | autoAccept.ToSwitchInput().WithField().Label("Auto-Accept Assigned Issues")
            | checkInterval.ToSelectInput(intervalOptions).WithField().Label("Check Interval")
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Check Now")
                    .Icon(Icons.RefreshCw)
                    .Outline()
                    .Small()
                    .Loading(isCheckingNow.Value)
                    .OnClick(async () =>
                    {
                        if (autoImportService != null)
                        {
                            isCheckingNow.Set(true);
                            try
                            {
                                await autoImportService.TriggerManualCheckAsync();
                                client.Toast("Checked for assigned issues", "Auto-Accept");
                                await onRefresh();
                            }
                            catch (Exception ex)
                            {
                                client.Toast($"Check failed: {ex.Message}", "Error");
                            }
                            finally
                            {
                                isCheckingNow.Set(false);
                            }
                        }
                    }));

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Auto-Accept Settings"),
            new DialogBody(dialogBody),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Save").Primary().OnClick(() =>
                {
                    config.Settings.Inbox.AutoAcceptAssignedIssues = autoAccept.Value;
                    config.Settings.Inbox.CheckIntervalMinutes = checkInterval.Value;
                    config.SaveSettings();
                    client.Toast("Auto-accept settings saved", "Saved");
                    isOpen.Set(false);
                    refreshToken.Refresh();
                })
            )
        );
    }
}
