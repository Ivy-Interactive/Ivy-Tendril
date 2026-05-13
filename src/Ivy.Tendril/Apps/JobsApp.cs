using System.Reactive.Linq;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views.Sheets;

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
        var openFile = UseState<string?>(null);
        var outputStream = UseStream<string>();
        var lastProcessedIndex = UseState(0);
        var activeOutputJobId = UseState<string?>(null);
        var streamingJobId = UseState<string?>(null);
        var hasStreamContent = UseState(false);

        var (planSheet, showPlan) = UseTrigger<string>((isOpen, planPath) =>
        {
            if (!isOpen.Value) return null;
            var planSheetView = new PlanSheet(planPath, planService, openFile);
            var sheet = new Sheet(
                () => isOpen.Set(false),
                planSheetView.Build(),
                planSheetView.GetSheetTitle()
            ).Width(Size.Half()).Resizable();
            return new Fragment(sheet, new FileSheet(openFile, config));
        });

        var (outputSheet, showOutput) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value)
            {
                activeOutputJobId.Set(null);
                streamingJobId.Set(null);
                hasStreamContent.Set(false);
                lastProcessedIndex.Set(0);
                return null;
            }
            activeOutputJobId.Set(jobId);
            var outputSheetView = new OutputSheet(jobId, jobService, outputStream, hasStreamContent, streamingJobId);
            return new Sheet(
                () => isOpen.Set(false),
                outputSheetView.Build(),
                outputSheetView.GetSheetTitle()
            ).Width(Size.Half()).Resizable();
        });

        var (promptSheet, showPrompt) = UseTrigger<string>((isOpen, promptText) =>
        {
            if (!isOpen.Value) return null;
            var promptSheetView = new PromptSheet(promptText);
            return new Sheet(
                () => isOpen.Set(false),
                promptSheetView.Build(),
                "Full Prompt"
            ).Width(Size.Half()).Resizable();
        });

#if DEBUG
        var (debugSheet, showDebug) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            var debugSheetView = new JobDebugSheet(jobId, jobService, planService, config);
            return new Sheet(
                () => isOpen.Set(false),
                debugSheetView.Build(),
                "Job Debug"
            ).Width(Size.Half()).Resizable();
        });
#endif

        UseInterval(() => StreamOutputLines(jobService, activeOutputJobId, streamingJobId, lastProcessedIndex, hasStreamContent, outputStream),
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

#if DEBUG
        var dataTable = BuildDataTable(rows, refreshToken, updateStream, config, planService,
            jobService, client, showPlan, showOutput, showPrompt, showDebug, jobs, projectColors, jobsProgress);
#else
        var dataTable = BuildDataTable(rows, refreshToken, updateStream, config, planService,
            jobService, client, showPlan, showOutput, showPrompt, null, jobs, projectColors, jobsProgress);
#endif

        var layout = Layout.Vertical().Height(Size.Full());

#if DEBUG
        return layout | new Fragment(dataTable, planSheet, outputSheet, promptSheet, debugSheet);
#else
        return layout | new Fragment(dataTable, planSheet, outputSheet, promptSheet);
#endif
    }
}
