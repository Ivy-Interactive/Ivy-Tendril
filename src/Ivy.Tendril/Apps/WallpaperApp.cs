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
        var versionService = UseService<IVersionCheckService>();
        var planDbService = UseService<IPlanDatabaseService>();
        var tunnelService = UseService<ICloudflaredService>();
        var versionInfo = UseState<VersionInfo?>(null);
        var dismissedVersion = UseState<string?>(null);
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
        }, []);

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
            var tunnelQr = new FloatingPanel(
                new Card(
                    Layout.Vertical().Gap(2).AlignContent(Align.Center)
                    | new QRCode { Value = tunnelAddress, PixelSize = 160, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium }
                    | Text.Block("Scan to open Tendril on your phone").Muted().Small()
                ).Header("Tunnel", null, Icons.QrCode)
            ).AlignSelf(Align.TopRight).Offset(new Thickness(0, 8, 8, 0));

            elements.Add(tunnelQr);
        }

        if (versionInfo.Value?.HasUpdate == true && versionInfo.Value.LatestVersion != dismissedVersion.Value)
        {
            var updateCommand = "dotnet tool update -g Ivy.Tendril";
            var notification = new FloatingPanel(
                new Card(
                    Layout.Vertical()
                    | Text.Rich()
                        .Bold($"v{versionInfo.Value.LatestVersion}")
                        .Run($" is available (you have v{versionInfo.Value.CurrentVersion})")
                        .Small()
                    | new CodeBlock(updateCommand, Languages.Bash)
                    | new Button("Dismiss", () => dismissedVersion.Set(versionInfo.Value.LatestVersion))
                        .Variant(ButtonVariant.Secondary)
                        .Small()
                ).Header("Update Available", null, Icons.CircleArrowUp)
            ).Offset(new Thickness(0, 0, 8, 8));

            elements.Add(notification);
        }

        return new Fragment(elements.ToArray());
    }

}
