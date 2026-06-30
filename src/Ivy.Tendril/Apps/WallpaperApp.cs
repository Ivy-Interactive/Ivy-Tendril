using System;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Widgets.ActivityHeatmap;
using Ivy.Widgets.QRCode;

namespace Ivy.Tendril.Apps;

[App(isVisible: false)]
public class WallpaperApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var versionService = UseService<IVersionCheckService>();
        var planDbService = UseService<IPlanDatabaseService>();
        var tunnelService = UseService<ICloudflaredService>();
        var copyToClipboard = UseClipboard();
        var versionInfo = UseState<VersionInfo?>(null);
        var dismissedVersion = UseState<string?>(() => config.Settings.DismissedUpdateVersion);
        var updateProgress = UseState<int?>(null);
        var updateStatus = UseState<string?>(null);
        var updateError = UseState<string?>(null);
        var tunnelStatus = UseState(tunnelService.Status);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);

        var processView = Context.UseTendrilProcess();

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                var info = await versionService.CheckForUpdatesAsync();
                versionInfo.Set(info);
            });
        });

        UseEffect(() =>
        {
            void OnStatusChanged(TunnelStatus newStatus)
            {
                tunnelStatus.Set(newStatus);
                tunnelUrl.Set(tunnelService.TunnelUrl);
            }

            tunnelService.StatusChanged += OnStatusChanged;

            tunnelStatus.Set(tunnelService.Status);
            tunnelUrl.Set(tunnelService.TunnelUrl);

            return Disposable.Create(() => tunnelService.StatusChanged -= OnStatusChanged);
        });

        // Query last 90 days of completed PRs
        var prData = planDbService.GetCompletedPrsByDay(90);
        var activities = prData.Select(x => new Activity { Date = x.Date, Count = x.Count }).ToArray();

        // Build vertical layout conditionally including heatmap only if there are PRs
        var verticalLayout = Layout.Vertical().Gap(2).AlignContent(Align.Center)
            | new Image("/tendril/assets/Tendril.svg").Width(Size.Units(30)).Height(Size.Auto())
            | Text.H2("What are we making next?")
            | processView;

        if (activities.Length > 0)
        {
            verticalLayout |= new Spacer().Height(Size.Units(5));
            verticalLayout |= new ActivityHeatmap()
                .Data(activities)
                .ShowMonthLabels(true)
                .ShowDayLabels(true)
                .StartDate(DateOnly.FromDateTime(DateTime.Today.AddDays(-89)))
                .EndDate(DateOnly.FromDateTime(DateTime.Today))
                .ValueLabel("PRs");
        }

        var elements = new List<object>
        {
            Layout.Center() | verticalLayout
        };

        // Only show the tunnel QR once the tunnel is fully established and routable
        // (Status == Connected), never during the Connecting phase.
        if (tunnelStatus.Value == TunnelStatus.Connected && tunnelUrl.Value is { } tunnelAddress)
        {
            var tunnelMenu = new Button().Icon(Icons.Ellipsis).Ghost().Small().WithDropDown(
                new MenuItem("Copy to Clipboard", Icon: Icons.ClipboardCopy, Tag: "copy").OnSelect(() =>
                {
                    copyToClipboard(tunnelAddress);
                    client.Toast("Tunnel URL copied to clipboard", "URL Copied");
                }),
                new MenuItem("Open in Browser", Icon: Icons.ExternalLink, Tag: "open").OnSelect(() => client.OpenUrl(tunnelAddress)),
                new MenuItem("Deactivate", Icon: Icons.Power, Tag: "deactivate").OnSelect(() =>
                {
                    // Optimistically remove the panel; the teardown runs in the background.
                    tunnelStatus.Set(TunnelStatus.Disabled);
                    client.Toast("Tunnel stopped", "Deactivated");
                    _ = tunnelService.DeactivateAsync();
                })
            );

            var tunnelQr = new FloatingPanel(
                new Card(
                    new QRCode { Value = tunnelAddress, PixelSize = 160, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium }
                ).Header("Tunnel", null, tunnelMenu)
            ).AlignSelf(Align.TopRight).Offset(new Thickness(0, 8, 8, 0));

            elements.Add(tunnelQr);
        }

        if ((versionInfo.Value?.HasUpdate == true && versionInfo.Value.LatestVersion != dismissedVersion.Value) || updateProgress.Value != null || updateError.Value != null)
        {
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
                    ? "irm https://cdn.ivy.app/install-tendril.ps1 | iex"
                    : "curl -sSf https://cdn.ivy.app/install-tendril.sh | sh";

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
                actions |= dismissButton;

                content = Layout.Vertical()
                    | Text.Rich()
                        .Bold($"v{versionInfo.Value!.LatestVersion}")
                        .Run($" is available (you have v{versionInfo.Value.CurrentVersion})")
                        .Small()
                    | new CodeBlock(updateCommand, Languages.Bash)
                    | actions;
            }

            var notification = new FloatingPanel(
                new Card(content).Header("Update Available", null, Icons.CircleArrowUp)
            ).Offset(new Thickness(0, 0, 8, 8));

            elements.Add(notification);
        }

        return new Fragment(elements.ToArray());
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

                // Reached only when no restart occurred (already up to date or self-update unavailable).
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
