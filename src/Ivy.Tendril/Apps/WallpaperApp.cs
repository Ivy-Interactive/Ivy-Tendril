using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps;

[App(isVisible: false)]
public class WallpaperApp : ViewBase
{
    public override object Build()
    {
        var countsService = UseService<IPlanCountsService>();
        var versionService = UseService<IVersionCheckService>();
        var versionInfo = UseState<VersionInfo?>(null);
        var dismissedVersion = UseState<string?>(null);
        var copyToClipboard = UseClipboard();

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                var info = await versionService.CheckForUpdatesAsync();
                versionInfo.Set(info);
            });
        }, []);

        var counts = countsService.Current;

        var hasActivity = counts.TotalPlans > 0;

        var heading = hasActivity ? "What are we making next?" : "Welcome to Ivy Tendril";
        var subtitle = hasActivity ? BuildSummary(counts) : "Manage your plans, track jobs, and review pull requests.";
        var buttonLabel = hasActivity ? "New Plan" : "Create your first plan";

        var elements = new List<object>
        {
            Layout.Center()
                | (Layout.Vertical().Gap(2).AlignContent(Align.Center)
                   | new Image("/tendril/assets/Tendril.svg").Width(Size.Units(30)).Height(Size.Auto())
                   | Text.H2(heading)
                   | Text.Muted(subtitle)
                   | new CreatePlanDialogLauncher(
                       open => new Button(buttonLabel, open)
                           .Variant(ButtonVariant.Primary)
                           .Icon(Icons.Plus, Align.Right))
                )
        };

        if (versionInfo.Value?.HasUpdate == true && versionInfo.Value.LatestVersion != dismissedVersion.Value)
        {
            var updateCommand = "dotnet tool update -g Ivy.Tendril";
            var notification = new FloatingPanel(
                new Card(
                    Layout.Vertical().Gap(2)
                    | Text.Rich()
                        .Bold($"v{versionInfo.Value.LatestVersion}")
                        .Run($" is available (you have v{versionInfo.Value.CurrentVersion})")
                        .Small()
                    | (Layout.Horizontal().Gap(1)
                        | new Button("Copy update command", () => copyToClipboard(updateCommand))
                            .Variant(ButtonVariant.Primary)
                            .Small()
                            .Icon(Icons.Clipboard)
                        | new Button("Dismiss", () => dismissedVersion.Set(versionInfo.Value.LatestVersion))
                            .Variant(ButtonVariant.Ghost)
                            .Small())
                ).Header("Update Available", null, Icons.CircleArrowUp),
                Align.BottomRight
            ).Offset(new Thickness(0, 0, 20, 20));

            elements.Add(notification);
        }

        return new Fragment(elements.ToArray());
    }

    private static string BuildSummary(PlanCounts counts)
    {
        var parts = new List<string>();

        if (counts.Drafts > 0)
            parts.Add($"{counts.Drafts} {(counts.Drafts == 1 ? "draft" : "drafts")}");
        if (counts.ActiveJobs > 0)
            parts.Add($"{counts.ActiveJobs} {(counts.ActiveJobs == 1 ? "job" : "jobs")} running");
        if (counts.Reviews > 0)
            parts.Add($"{counts.Reviews} {(counts.Reviews == 1 ? "review" : "reviews")} waiting");

        return parts.Count > 0
            ? "You have " + string.Join(", ", parts) + "."
            : "No current drafts, jobs or reviews.";
    }
}
