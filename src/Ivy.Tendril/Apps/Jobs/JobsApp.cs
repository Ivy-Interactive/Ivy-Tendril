using System.Reactive.Linq;
using Ivy.Tendril.Apps.Drafts.Dialogs;
using Ivy.Tendril.Apps.Jobs.Dialogs;
using Ivy.Tendril.Apps.Jobs.Sheets;
using Ivy.Tendril.Apps.Review.Dialogs;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Jobs;

[App(title: "Jobs", icon: Icons.Activity, group: ["Apps"], order: Constants.Jobs)]
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
        var selectedView = UseState(0);

        // Board card id (plan folder name or job id) whose detail sheet is open, so
        // the matching card renders in its selected state. Cleared when the sheet closes.
        var selectedCardId = UseState<string?>(null);

        // Closing a board card's detail sheet also clears the selected-card highlight.
        // selectedCardId lives in this (parent) scope, so setting it here re-renders
        // the board.
        void CloseCardSheet(IState<bool> isOpen)
        {
            isOpen.Set(false);
            selectedCardId.Set((string?)null);
        }

        var (planSheet, showPlan) = UseTrigger<string>((isOpen, planPath) =>
        {
            if (!isOpen.Value) return null;
            var planSheetView = new PlanSheet(planPath, planService, openFile, config);
            var sheet = new Sheet(
                () => CloseCardSheet(isOpen),
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
                () => CloseCardSheet(isOpen),
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

        var boardPlanState = UseState<PlanFile?>(null);

        var (updatePlanDialog, showUpdatePlan) = UseTrigger<string>((isOpen, folderName) =>
        {
            if (!isOpen.Value) return null;
            var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
            if (plan == null) return null;
            return new UpdatePlanDialog(isOpen, plan, boardPlanState, jobService, () => refreshToken.Refresh());
        });

        var (deletePlanDialog, showDeletePlan) = UseTrigger<string>((isOpen, folderName) =>
        {
            if (!isOpen.Value) return null;
            var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
            if (plan == null) return null;
            return new DeletePlanDialog(isOpen, plan, boardPlanState, planService, () => refreshToken.Refresh());
        });

        // Board card / drag actions reusing ReviewApp's plan dialogs, keyed by folder name.
        var (suggestChangesDialog, showSuggestChanges) = UseTrigger<string>((isOpen, folderName) =>
        {
            if (!isOpen.Value) return null;
            var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
            if (plan == null) return null;
            return new SuggestChangesDialog(isOpen, plan, jobService, () => refreshToken.Refresh());
        });

        var (createPrDialog, showCreatePr) = UseTrigger<string>((isOpen, folderName) =>
        {
            if (!isOpen.Value) return null;
            var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
            if (plan == null) return null;
            return new CreatePrDialogHost(isOpen, plan, jobService, config, () => refreshToken.Refresh());
        });

        var resetToDraftLogger = UseService<ILogger<ResetToDraftDialog>>();
        var (resetToDraftDialog, showResetToDraft) = UseTrigger<string>((isOpen, folderName) =>
        {
            if (!isOpen.Value) return null;
            var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
            if (plan == null) return null;
            return new ResetToDraftDialog(isOpen, plan, planService, () => refreshToken.Refresh(),
                resetToDraftLogger);
        });

        var (discardPlanDialog, showDiscardPlan) = UseTrigger<string>((isOpen, folderName) =>
        {
            if (!isOpen.Value) return null;
            var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
            if (plan == null) return null;
            return new DiscardPlanDialog(isOpen, plan, planService, () => refreshToken.Refresh());
        });

        var (deleteJobDialog, showDeleteJob) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            return new Dialog(
                _ => isOpen.Set(false),
                new DialogHeader("Delete Job"),
                new DialogBody(Text.P("Are you sure you want to delete this job? A running job is stopped first.")),
                new DialogFooter(
                    new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                    new Button("Delete").Destructive().ShortcutKey("Enter").AutoFocus().OnClick(() =>
                    {
                        var job = jobService.GetJob(jobId);
                        if (job?.Status is JobStatus.Running or JobStatus.Queued)
                            jobService.StopJob(jobId);
                        jobService.DeleteJob(jobId);
                        isOpen.Set(false);
                        refreshToken.Refresh();
                    })
                ));
        });

        UseEffect(() => JobsApp.JobChangeHookDisposable(jobService, refreshToken));
        UseInterval(() => JobsApp.AutoRefreshCheck(jobService, refreshToken), TimeSpan.FromSeconds(5));

        var updateStream = UseDataTableUpdates(
            Observable.Interval(TimeSpan.FromSeconds(1))
                .SelectMany(_ => JobsApp.BuildDataTableUpdates(jobService)));

        var jobs = jobService.GetJobs();
        var projectColors = BuildProjectColorMapping(config);
        var rows = BuildJobRows(jobs, planService);
        var jobsProgress = jobs.Count > 0 ? BuildStatusProgress(jobs, config) : null;

        var dataTable = JobsApp.BuildDataTable(nav, rows, refreshToken, updateStream, config, planService,
            jobService, client, showPlan, showOutput, showPrompt, showDebug, showRerun, jobs, projectColors, jobsProgress,
            confirmDeleteOpen, deleteJobId);

        var board = BuildBoard(
            jobs,
            planService,
            jobService,
            projectColors,
            nav,
            selectedCardId.Value,
            new BoardActions(
                OnJobClick: jobId => showOutput(jobId),
                ShowPlanSheet: planPath => showPlan(planPath),
                ShowUpdatePlan: showUpdatePlan,
                ShowDeletePlan: showDeletePlan,
                ShowDebugJob: showDebug,
                ShowRerunJob: showRerun,
                ShowDeleteJob: showDeleteJob,
                ShowSuggestChanges: showSuggestChanges,
                ShowCreatePr: showCreatePr,
                ShowResetToDraft: showResetToDraft,
                ShowDiscardPlan: showDiscardPlan,
                SetSelected: id => selectedCardId.Set(id),
                Refresh: () => refreshToken.Refresh()));

        var content = Layout.Tabs(
                new Tab("Table", dataTable),
                new Tab("Board", board)
            )
            .OnSelect(v => selectedView.Set(v))
            .SelectedIndex(selectedView.Value)
            .Variant(TabsVariant.Content)
            .RemoveParentPadding();

        var layout = Layout.Vertical().Height(Size.Full());

        return layout | new Fragment(content, planSheet, outputSheet, promptSheet, debugSheet, rerunDialog,
            updatePlanDialog, deletePlanDialog, suggestChangesDialog, createPrDialog, resetToDraftDialog,
            discardPlanDialog, deleteJobDialog);
    }
}
