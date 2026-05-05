using System.Reactive.Linq;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

[App(title: "Jobs", icon: Icons.Activity, group: ["Apps"], order: Constants.Jobs)]
public partial class JobsApp : ViewBase
{
    public override object Build()
    {
        var jobService = UseService<IJobService>();
        var planService = UseService<IPlanReaderService>();
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var refreshToken = UseRefreshToken();
        var showPlan = UseState<string?>(null);
        var showOutput = UseState<string?>(null);
        var showPrompt = UseState<string?>(null);
        var openFile = UseState<string?>(null);
        var outputStream = UseStream<string>();
        var lastProcessedIndex = UseState(0);
        var streamingJobId = UseState<string?>(null);
        var hasStreamContent = UseState(false);

        UseInterval(() => StreamOutputLines(jobService, showOutput, streamingJobId, lastProcessedIndex, hasStreamContent, outputStream),
            TimeSpan.FromMilliseconds(100));
        UseEffect(() => NotificationHookDisposable(jobService, client));
        UseEffect(() => JobChangeHookDisposable(jobService, refreshToken));
        UseInterval(() => AutoRefreshCheck(jobService, refreshToken), TimeSpan.FromSeconds(5));

        var updateStream = UseDataTableUpdates(
            Observable.Interval(TimeSpan.FromSeconds(1))
                .SelectMany(_ => BuildDataTableUpdates(jobService)));

        var jobs = jobService.GetJobs();
        var projectColors = BuildProjectColorMapping(config);
        var rows = BuildJobRows(jobs, planService);
        var jobsProgress = BuildStatusProgress(jobs, config);

        var dataTable = BuildDataTable(rows, refreshToken, updateStream, config, planService,
            jobService, client, showPlan, showOutput, showPrompt, jobs, projectColors, jobsProgress);

        var layout = Layout.Vertical().Height(Size.Full());

        return RenderWithSheets(dataTable, showPlan, showOutput, showPrompt, planService,
            config, openFile, jobService, outputStream, hasStreamContent, streamingJobId, layout);
    }
}
