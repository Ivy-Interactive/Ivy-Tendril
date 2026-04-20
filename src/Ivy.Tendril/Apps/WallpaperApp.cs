using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps;

[App(isVisible: false)]
public class WallpaperApp : ViewBase
{
    public override object Build()
    {
        var countsService = UseService<IPlanCountsService>();
        var versionService = UseService<IVersionCheckService>();
        var weatherService = UseService<IWeatherService>();
        var versionInfo = UseState<VersionInfo?>(null);
        var weatherInfo = UseState<WeatherInfo?>(null);

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                var info = await versionService.CheckForUpdatesAsync();
                versionInfo.Set(info);
            });

            _ = Task.Run(async () =>
            {
                var weather = await weatherService.GetWeatherAsync();
                weatherInfo.Set(weather);
            });
        }, []);

        var counts = countsService.Current;

        var hasActivity = counts.TotalPlans > 0;

        var heading = hasActivity ? "What are we making next?" : "Welcome to Ivy Tendril";
        var subtitle = hasActivity ? BuildSummary(counts) : "Manage your plans, track jobs, and review pull requests.";
        var buttonLabel = hasActivity ? "New Plan" : "Create your first Plan";

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

        if (versionInfo.Value?.HasUpdate == true)
            elements.Insert(0, new Box(
                Callout.Info(
                    $"**Tendril v{versionInfo.Value.LatestVersion} is available!**\n\n" +
                    $"You're currently running v{versionInfo.Value.CurrentVersion}.\n\n" +
                    $"Update with: `tendril --version && dotnet tool update -g Ivy.Tendril`",
                    "Update Available"
                )
            ).Margin(2));

        if (weatherInfo.Value != null)
            elements.Insert(0, new Box(
                Callout.Success(
                    $"{weatherInfo.Value.Icon} **{weatherInfo.Value.Temperature}** · {weatherInfo.Value.Condition}",
                    weatherInfo.Value.Location
                )
            ).Margin(2));

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

        return "You have " + string.Join(", ", parts) + ".";
    }
}
