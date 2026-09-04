using System;
using System.Threading.Tasks;
using Ivy.Tendril.AppShell.Dialogs;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Views;

/// <summary>
/// The "new version available" card. The default layout spells everything out;
/// <paramref name="compact"/> (used by the dashboard's fixed-height update slot)
/// shows only the alert with a primary action and a "Show Details" button that
/// opens <see cref="UpdateTendrilDialog"/> with the full description and command.
/// </summary>
public class UpdateNoticeView(bool floating = false, bool compact = false) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var versionService = UseService<IVersionCheckService>();
        var copyToClipboard = UseClipboard();
        var versionInfo = UseState(() => versionService.CachedVersionInfo);
        var dismissedVersion = UseState<string?>(() => config.Settings.DismissedUpdateVersion);
        var updateProgress = UseState<int?>(null);
        var updateStatus = UseState<string?>(null);
        var updateError = UseState<string?>(null);
        var detailsOpen = UseState(false);

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                var info = await versionService.CheckForUpdatesAsync();
                versionInfo.Set(info);
            });
        });

        var hasPendingUpdate = versionInfo.Value?.HasUpdate == true
                               && versionInfo.Value.LatestVersion != dismissedVersion.Value;
        if (!hasPendingUpdate && updateProgress.Value == null && updateError.Value == null)
        {
            return null;
        }

        object content;

        if (updateError.Value is { } errorMsg)
        {
            content = Layout.Vertical()
                | Text.Danger("Update Failed").Bold().Small()
                | Text.Block(errorMsg).Small()
                | Layout.Horizontal().Gap(2)
                    | new Button("Retry", () =>
                        {
                            TriggerUpdate(versionService, updateProgress, updateStatus, updateError);
                        })
                        .Small()
                    | new Button("Dismiss", () =>
                        {
                            updateError.Set(null);
                            var latest = versionInfo.Value?.LatestVersion ?? "";
                            if (!string.IsNullOrEmpty(latest))
                            {
                                dismissedVersion.Set(latest);
                                config.Settings.DismissedUpdateVersion = latest;
                                config.SaveSettings();
                            }
                        })
                        .Variant(ButtonVariant.Secondary)
                        .Small();
        }
        else if (updateProgress.Value is { } progressVal)
        {
            content = Layout.Vertical()
                | Text.Block(updateStatus.Value ?? "Updating...").Small()
                | new Progress(progressVal)
                | Text.Muted($"{progressVal}%").Small();
        }
        else if (compact)
        {
            var updateCommand = OperatingSystem.IsWindows()
                ? Constants.WindowsInstallCommand
                : Constants.UnixInstallCommand;

            var primaryAction = versionService.CanSelfUpdate
                ? new Button("Update Now", () =>
                    {
                        TriggerUpdate(versionService, updateProgress, updateStatus, updateError);
                    })
                    .Small()
                : new Button("Copy Command", () =>
                    {
                        copyToClipboard(updateCommand);
                        client.Toast("Update command copied to clipboard", "Copied");
                    })
                    .Small();

            content = Layout.Vertical().Gap(2)
                | Text.Rich()
                    .Bold("Update Available")
                    .Run($" — v{versionInfo.Value!.LatestVersion}")
                    .Small()
                | (Layout.Horizontal().Gap(2)
                   | primaryAction
                   | new Button("Show Details", () => detailsOpen.Set(true))
                       .Variant(ButtonVariant.Secondary)
                       .Small());
        }
        else
        {
            var updateCommand = OperatingSystem.IsWindows()
                ? Constants.WindowsInstallCommand
                : Constants.UnixInstallCommand;

            var dismissButton = new Button("Dismiss", () =>
                {
                    var latest = versionInfo.Value!.LatestVersion;
                    dismissedVersion.Set(latest);
                    config.Settings.DismissedUpdateVersion = latest;
                    config.SaveSettings();
                })
                .Variant(ButtonVariant.Secondary)
                .Small();

            var actions = Layout.Horizontal().Gap(2);
            if (versionService.CanSelfUpdate)
            {
                actions |= new Button("Update Now", () =>
                    {
                        TriggerUpdate(versionService, updateProgress, updateStatus, updateError);
                    })
                    .Small();
            }
            else
            {
                actions |= new Button("Copy Command", () =>
                    {
                        copyToClipboard(updateCommand);
                        client.Toast("Update command copied to clipboard", "Copied");
                    })
                    .Small();
            }
            actions |= dismissButton;

            var verticalContent = Layout.Vertical()
                | Text.Rich()
                    .Bold($"v{versionInfo.Value!.LatestVersion}")
                    .Run($" is available (you have v{versionInfo.Value.CurrentVersion})")
                    .Small();

            if (versionService.CanSelfUpdate)
            {
                verticalContent |= Text.Block("Click Update Now to download and install automatically. Tendril will restart.").Small();
            }
            else
            {
                verticalContent |= Text.Block("Run this command in your terminal to update:").Small();
                verticalContent |= new CodeBlock(updateCommand, Languages.Bash);
            }

            verticalContent |= actions;
            content = verticalContent;
        }

        if (compact)
        {
            // The dashboard's update slot is a fixed 120px (header height); the
            // headerless card fills it exactly so appearing never shifts layout.
            var compactCard = new Card(content).Height(Size.Full());
            return versionInfo.Value is { } info
                ? new Fragment(compactCard, new UpdateTendrilDialog(detailsOpen, info))
                : compactCard;
        }

        var card = new Card(content).Header("Update Available", null, Icons.CircleArrowUp);

        return floating
            ? new FloatingPanel(card).Offset(new Thickness(0, 0, 8, 8))
            : card;
    }

    private void TriggerUpdate(IVersionCheckService versionService, IState<int?> updateProgress, IState<string?> updateStatus, IState<string?> updateError)
    {
        updateProgress.Set(0);
        updateStatus.Set("Starting update...");
        updateError.Set(null);

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new UpdateProgress(
                    p => updateProgress.Set(p),
                    s => updateStatus.Set(s));

                await versionService.StartUpdateAsync(progress);

                updateProgress.Set(null);
            }
            catch (Exception ex)
            {
                updateError.Set(ex.Message);
                updateProgress.Set(null);
            }
        });
    }
}
