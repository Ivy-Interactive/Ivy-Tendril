using System.Reactive.Disposables;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Widgets.TendrilProcessView;

namespace Ivy.Tendril.Apps;

[App(isVisible: false)]
public class WallpaperApp : ViewBase
{
    public override object Build()
    {
        var statusService = UseService<ITendrilProcessStatusService>();
        var versionService = UseService<IVersionCheckService>();
        var navigator = UseNavigation();
        var versionInfo = UseState<VersionInfo?>(null);
        var dismissedVersion = UseState<string?>(null);
        var processStatus = UseState(() => statusService.Current);

        UseEffect(() =>
        {
            var subscription = statusService.Status.Subscribe(s => processStatus.Set(s));
            return Disposable.Create(() => subscription.Dispose());
        });

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                var info = await versionService.CheckForUpdatesAsync();
                versionInfo.Set(info);
            });
        }, []);

        var status = processStatus.Value;

        var processView = new TendrilProcessView()
            .DraftCount(status.DraftCount)
            .ReviewCount(status.ReviewCount)
            .CreatingPlansCount(status.CreatingPlansCount)
            .UpdatingPlansCount(status.UpdatingPlansCount)
            .ExecutingPlansCount(status.ExecutingPlansCount)
            .RetryingPlansCount(status.RetryingPlansCount)
            .OnCreate(() => navigator.Navigate<DraftsApp>())
            .OnDrafts(() => navigator.Navigate<DraftsApp>())
            .OnReview(() => navigator.Navigate<ReviewApp>())
            .OnJobs(() => navigator.Navigate<JobsApp>());

        var elements = new List<object>
        {
            Layout.Center()
                | (Layout.Vertical().Gap(2).AlignContent(Align.Center)
                   | new Image("/tendril/assets/Tendril.svg").Width(Size.Units(30)).Height(Size.Auto())
                   | Text.H2("What are we making next?")
                   | processView
                )
        };

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
