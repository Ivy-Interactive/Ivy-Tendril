using System.Linq;
using System.Reactive.Linq;
using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Jobs.Dialogs;
using Ivy.Tendril.Apps.Jobs.Sheets;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

[App(title: "Jobs", icon: Icons.Activity, group: ["Automations"], order: Constants.Jobs)]
public partial class JobsApp : ViewBase
{
    public override object Build()
    {
        var jobService = UseService<IJobService>();
        var planService = UseService<IPlanReaderService>();
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var nav = UseNavigation();
        var refreshToken = UseRefreshToken();
        var openFile = UseState<string?>(null);
        var confirmDeleteOpen = UseState(false);
        var deleteJobId = UseState<string?>(null);
        var selectedStatus = UseState("All");
        var selectedType = UseState("All");

        var (planSheet, showPlan) = UseTrigger<string>((isOpen, planPath) =>
        {
            if (!isOpen.Value) return null;
            var planSheetView = new PlanSheet(planPath, planService, openFile, config);
            var sheet = new Sheet(
                () => isOpen.Set(false),
                planSheetView.Build(),
                planSheetView.GetSheetTitle()
            ).Width(UxHelper.SheetWidth).Resizable();
            return new Fragment(sheet, new FileSheet(openFile, config));
        });

        var (outputSheet, showOutput) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            var job = jobService.GetJob(jobId);
            var title = job is not null ? $"{job.Type} {JobsApp.ExtractPlanId(job.PlanFile)}" : "Job Output";
            return new Sheet(
                () => isOpen.Set(false),
                new OutputSheet(jobId, jobService),
                title
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var (promptSheet, showPrompt) = UseTrigger<string>((isOpen, promptText) =>
        {
            if (!isOpen.Value) return null;
            var promptSheetView = new PromptSheet(promptText);
            return new Sheet(
                () => isOpen.Set(false),
                promptSheetView.Build(),
                "Full Prompt"
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var (debugSheet, showDebug) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            return new Sheet(
                () => isOpen.Set(false),
                new JobDebugSheet(jobId, jobService, planService, config, () => isOpen.Set(false)),
                "Job Debug"
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var (rerunDialog, showRerun) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            var job = jobService.GetJob(jobId);
            if (job == null) return null;
            return new RerunJobDialog(isOpen, job, jobService, () => refreshToken.Refresh());
        });

        UseEffect(() => JobsApp.JobChangeHookDisposable(jobService, refreshToken));
        UseInterval(() => JobsApp.AutoRefreshCheck(jobService, refreshToken), TimeSpan.FromSeconds(5));

        var updateStream = UseDataTableUpdates(
            Observable.Interval(TimeSpan.FromSeconds(1))
                .SelectMany(_ => JobsApp.BuildDataTableUpdates(jobService)));

        var jobs = jobService.GetJobs();

        var availableTypes = new List<string> { "All" };
        availableTypes.AddRange(jobs.Select(j => j.Type).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t));

        var statusOptions = new List<string> { "All" };
        statusOptions.AddRange(Enum.GetNames<JobStatus>());

        var filteredJobs = jobs;
        if (selectedStatus.Value != "All" && Enum.TryParse<JobStatus>(selectedStatus.Value, out var filterStatus))
        {
            filteredJobs = filteredJobs.Where(j => j.Status == filterStatus).ToList();
        }
        if (selectedType.Value != "All")
        {
            filteredJobs = filteredJobs.Where(j => j.Type == selectedType.Value).ToList();
        }

        var projectColors = BuildProjectColorMapping(config);
        var rows = BuildJobRows(filteredJobs, planService);
        var jobsProgress = filteredJobs.Count > 0 ? BuildStatusProgress(filteredJobs, config) : null;

        var typeOptions = availableTypes
            .Select(t => new Option<string>(t == "All" ? "Type: All" : $"Type: {t}", t))
            .ToArray<IAnyOption>();

        var statusOptionsWithLabels = statusOptions
            .Select(s => new Option<string>(s == "All" ? "Status: All" : $"Status: {s}", s))
            .ToArray<IAnyOption>();

        var typeFilter = selectedType.ToSelectInput(typeOptions)
            .Width(Size.Px(180));

        var statusFilter = selectedStatus.ToSelectInput(statusOptionsWithLabels)
            .Width(Size.Px(180));

        var dataTable = JobsApp.BuildDataTable(nav, rows, refreshToken, updateStream, config, planService,
            jobService, client, showPlan, showOutput, showPrompt, showDebug, showRerun, jobs, projectColors, jobsProgress,
            confirmDeleteOpen, deleteJobId, typeFilter, statusFilter);

        var layout = Layout.Vertical().Height(Size.Full());

        return layout | new Fragment(dataTable, planSheet, outputSheet, promptSheet, debugSheet, rerunDialog);
    }
}
