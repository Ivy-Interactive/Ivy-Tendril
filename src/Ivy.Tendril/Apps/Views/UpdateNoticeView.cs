using System;
using System.Threading.Tasks;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Views;

public class UpdateNoticeView(bool floating = false) : ViewBase
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
