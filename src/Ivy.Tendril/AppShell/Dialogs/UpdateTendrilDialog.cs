using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.AppShell.Dialogs;

public class UpdateTendrilDialog(IState<bool> isOpen, VersionInfo info) : ViewBase
{
    public override object? Build()
    {
        var versionService = UseService<IVersionCheckService>();
        var updateProgress = UseState<int?>(null);
        var updateStatus = UseState<string?>(null);
        var updateError = UseState<string?>(null);

        if (!isOpen.Value) return null;

        void Close()
        {
            isOpen.Set(false);
        }

        void RunUpdate()
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

                    // Reached only when no update was applied (already up to date, or
                    // self-update unavailable for this install type). Otherwise, the app
                    // exits to apply and restart.
                    updateProgress.Set(null);
                }
                catch (Exception ex)
                {
                    updateError.Set(ex.Message);
                    updateProgress.Set(null);
                }
            });
        }

        object content;

        if (updateError.Value is { } errorMsg)
        {
            content = Layout.Vertical().Gap(2)
                | Text.Danger("Update Failed").Bold().Small()
                | Text.Block(errorMsg).Small()
                | Layout.Horizontal().Gap(2)
                    | new Button("Retry", RunUpdate).Small()
                    | new Button("Cancel", Close).Variant(ButtonVariant.Secondary).Small();
        }
        else if (updateProgress.Value is { } progressVal)
        {
            content = Layout.Vertical().Gap(3)
                | Text.Block(updateStatus.Value ?? "Updating...").Small()
                | new Progress(progressVal)
                | Text.Muted($"{progressVal}%").Small();
        }
        else
        {
            if (versionService.CanSelfUpdate)
            {
                content = Layout.Vertical().Gap(3)
                    | Text.Rich()
                        .Bold($"v{info.LatestVersion}")
                        .Run($" is available (you have v{info.CurrentVersion}).")
                        .Small()
                    | Text.Block("Click \"Update Now\" to automatically download and install the update. Tendril will automatically restart once finished.").Small();
            }
            else
            {
                var updateCommand = OperatingSystem.IsWindows()
                    ? "irm https://cdn.ivy.app/install-tendril.ps1 | iex"
                    : "curl -sSf https://cdn.ivy.app/install-tendril.sh | sh";

                content = Layout.Vertical().Gap(3)
                    | Text.Rich()
                        .Bold($"v{info.LatestVersion}")
                        .Run($" is available (you have v{info.CurrentVersion}).")
                        .Small()
                    | Text.Block("Automatic update is not available for this installation type. Run the following command in your terminal to update:").Small()
                    | new CodeBlock(updateCommand, Languages.Bash);
            }
        }

        var footerButtons = new List<object>();

        if (updateProgress.Value == null)
        {
            if (updateError.Value != null)
            {
                footerButtons.Add(new Button("Cancel").Outline().OnClick(Close));
                footerButtons.Add(new Button("Retry").Primary().OnClick(RunUpdate));
            }
            else if (versionService.CanSelfUpdate)
            {
                footerButtons.Add(new Button("Cancel").Outline().OnClick(Close));
                footerButtons.Add(new Button("Update Now").Primary().OnClick(RunUpdate));
            }
            else
            {
                footerButtons.Add(new Button("OK").Primary().OnClick(Close));
            }
        }
        else
        {
            footerButtons.Add(new Button("Updating...").Primary().Disabled());
        }

        return new Dialog(
            _ => { if (updateProgress.Value == null) Close(); },
            new DialogHeader("Update Tendril"),
            new DialogBody(content),
            new DialogFooter(footerButtons.ToArray())
        ).Width(Size.Rem(32));
    }
}
