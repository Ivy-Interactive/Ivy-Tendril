using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    private static object BuildDataTable(
        INavigator nav,
        List<JobItemRow> rows,
        RefreshToken refreshToken,
        IWriteStream<DataTableCellUpdate> updateStream,
        IConfigService config,
        IPlanReaderService planService,
        IJobService jobService,
        IClientProvider client,
        Action<string> showPlan,
        Action<string> showOutput,
        Action<string> showPrompt,
        Action<string>? showDebug,
        Action<string> showRerun,
        List<JobItem> jobs,
        Dictionary<string, string> projectColors,
        StackedProgress? jobsProgress,
        IState<bool> confirmDeleteOpen,
        IState<string?> deleteJobId)
    {
        var dataTable = rows.AsQueryable()
            .ToDataTable(t => t.Id)
            .Density(new Responsive<Density?> { Default = Density.Large, Desktop = Density.Medium })
            .RefreshToken(refreshToken)
            .UpdateStream(updateStream)
            .Width(Size.Full())
            .Height(Size.Full())
            .Header(t => t.Status, "Status")
            .Header(t => t.Type, "Type")
            .Header(t => t.PlanId, "Plan")
            .Header(t => t.Plan, "Prompt/Title")
            .Header(t => t.Project, "Project")
            .Header(t => t.Timer, "Timer")
            .Header(t => t.Cost, "Cost")
            .Header(t => t.Tokens, "Tokens")
            .Header(t => t.AgentOutput, "Agent Output")
            .Renderer(t => t.AgentOutput, new AnimatedStatusLabelDisplayRenderer
            {
                Mode = AnimatedStatusMode.SpinnerTimer
            })
            .Header(t => t.StatusMessage, "Status")
            .Width(t => t.Status, Size.Px(100))
            .Width(t => t.PlanId, Size.Px(80))
            .Width(t => t.Type, Size.Px(100))
            .Width(t => t.Plan, Size.Px(250))
            .Width(t => t.Project, Size.Px(150))
            .Width(t => t.Timer, Size.Px(80))
            .Width(t => t.AgentOutput, Size.Px(100))
            .Width(t => t.Cost, Size.Px(80))
            .Width(t => t.Tokens, Size.Px(80))
            .Width(t => t.StatusMessage, Size.Auto())
            .Renderer(t => t.Status, new AnimatedStatusLabelDisplayRenderer
            {
                Mode = AnimatedStatusMode.Badge,
                BadgeColorMapping = Constants.JobStatusColors.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value.ToString()
                )
            })
            .Renderer(t => t.Type, new LabelsDisplayRenderer
            {
                BadgeColorMapping = Constants.JobTypeColors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToString()
                )
            })
            .Renderer(t => t.Project, new LabelsDisplayRenderer
            {
                BadgeColorMapping = projectColors
            })
            .Renderer(t => t.Plan, new TextDisplayRenderer())
            .Renderer(t => t.StatusMessage, new TextDisplayRenderer())
            .Hidden(t => t.Id)
            .Hidden(t => t.LastOutputTimestamp)
            .Hidden(t => t.ErrorContext)
            .SortDirection(t => t.Id, SortDirection.Descending)
            .Config(c =>
            {
                c.AllowSorting = true;
                c.AllowFiltering = true;
                c.ShowSearch = false;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
                c.BatchSize = 50;
            })
            .OnCellAction(t => t.PlanId, e =>
            {
                var planId = e.Value.CellValue?.ToString();
                if (!string.IsNullOrEmpty(planId))
                {
                    var job = jobs.FirstOrDefault(j => JobsApp.ExtractPlanId(j.PlanFile) == planId);
                    if (job != null && !string.IsNullOrEmpty(job.PlanFile))
                    {
                        var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
                        if (Directory.Exists(fullPath))
                        {
                            // If job is active (Running/Queued/Pending/Blocked), always show in sheet
                            // to keep user in Jobs context
                            var isActiveJob = job.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked;

                            if (isActiveJob)
                            {
                                showPlan(fullPath);
                            }
                            else
                            {
                                var plan = planService.GetPlanByFolder(fullPath);
                                if (plan != null)
                                {
                                    if (plan.Status is PlanStatus.Draft or PlanStatus.Blocked)
                                    {
                                        nav.Navigate<DraftsApp>(new DraftsAppArgs(plan.FolderName));
                                    }
                                    else if (plan.Status is PlanStatus.Review or PlanStatus.Failed)
                                    {
                                        nav.Navigate<ReviewApp>(new ReviewAppArgs(plan.FolderName));
                                    }
                                    else
                                    {
                                        showPlan(fullPath);
                                    }
                                }
                                else
                                {
                                    showPlan(fullPath);
                                }
                            }
                        }
                    }
                }
                return ValueTask.CompletedTask;
            })
            .OnCellAction(t => t.AgentOutput, e =>
            {
                var id = e.Value.RowId?.ToString();
                if (!string.IsNullOrEmpty(id))
                    showOutput(id);
                return ValueTask.CompletedTask;
            })
            /*
            .OnCellAction(t => t.StatusMessage, e =>
            {
                var id = e.Value.RowId?.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    var job = jobs.FirstOrDefault(j => j.Id == id);
                    if (job?.Status is JobStatus.Failed or JobStatus.Timeout)
                    {
                        showOutput.Set(id);
                    }
                }
                return ValueTask.CompletedTask;
            })
            */
            .OnCellAction(t => t.Plan, e =>
            {
                var id = e.Value.RowId?.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    var job = jobs.FirstOrDefault(j => j.Id == id);
                    if (job != null)
                    {
                        var fullPrompt = JobsApp.GetFullPrompt(job, planService);
                        if (!string.IsNullOrEmpty(fullPrompt))
                            showPrompt(fullPrompt);
                    }
                }
                return ValueTask.CompletedTask;
            })
            .RowActions(row =>
            {
                var job = jobs.FirstOrDefault(j => j.Id == row.Id);
                var actions = new List<MenuItem> { };

                if (job?.Status is JobStatus.Running or JobStatus.Queued)
                {
                    actions.Add(new MenuItem("Stop", Icon: Icons.Square, Tag: "stop-job")
                        .Tooltip("Stop this running job"));
                }

                if (job?.Status is JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped)
                {
                    actions.Add(new MenuItem("Rerun", Icon: Icons.RotateCw, Tag: "rerun-job")
                        .Tooltip("Rerun this job"));
                }

                if (job?.Status is JobStatus.Blocked)
                {
                    actions.Add(new MenuItem("Force Start", Icon: Icons.Zap, Tag: "force-start-job")
                        .Tooltip("Force start this blocked job"));
                }

                if (showDebug != null)
                {
                    actions.Add(new MenuItem("Debug", Icon: Icons.Bug, Tag: "debug-job")
                        .Tooltip("Show debug details for this job"));
                }

                actions.Add(new MenuItem("Delete", Icon: Icons.Trash, Tag: "delete-job")
                    .Tooltip("Delete this job"));

                return actions.ToArray();
            })
            .OnRowAction(e =>
            {
                var tag = e.Value.Tag?.ToString();
                var id = e.Value.Id?.ToString();
                var job = jobs.FirstOrDefault(j => j.Id == id);

                if (job != null)
                {

                    if (tag == "stop-job")
                    {
                        if (job.Status is JobStatus.Running or JobStatus.Queued)
                        {
                            jobService.StopJob(job.Id);
                            refreshToken.Refresh();
                        }
                    }
                    else if (tag == "rerun-job")
                    {
                        if (job.Status is JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped)
                        {
                            if (job.TypedArgs == null)
                            {
                                client.Toast("Cannot rerun: original args were not preserved.", "Rerun Failed");
                                return ValueTask.CompletedTask;
                            }

                            showRerun(job.Id);
                        }
                    }
                    else if (tag == "force-start-job")
                    {
                        if (job.Status is JobStatus.Blocked)
                        {
                            jobService.ForceStartJob(job.Id);
                            refreshToken.Refresh();
                        }
                    }
                    else if (tag == "debug-job")
                    {
                        showDebug?.Invoke(job.Id);
                    }
                    else if (tag == "delete-job")
                    {
                        deleteJobId.Set(job.Id);
                        confirmDeleteOpen.Set(true);
                    }
                }

                return ValueTask.CompletedTask;
            })
            .HeaderRight(_ => Layout.Horizontal()
                              | (jobsProgress != null ? jobsProgress : null!)
                              | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                                  new MenuItem("Clear Completed", Icon: Icons.Trash, Tag: "ClearCompleted")
                                      .OnSelect(() =>
                                      {
                                          jobService.ClearCompletedJobs();
                                          refreshToken.Refresh();
                                      }),
                                  new MenuItem("Clear Failed", Icon: Icons.Trash, Tag: "ClearFailed").OnSelect(() =>
                                  {
                                      jobService.ClearFailedJobs();
                                      refreshToken.Refresh();
                                  }),
                                  new MenuItem("Clear All", Icon: Icons.Trash, Tag: "ClearAll").OnSelect(() =>
                                  {
                                      jobService.ClearAllJobs();
                                      refreshToken.Refresh();
                                  })
                              ));

        var confirmDialog = confirmDeleteOpen.Value ? new Dialog(
            _ => confirmDeleteOpen.Set(false),
            new DialogHeader("Delete Job"),
            new DialogBody(Text.P("Are you sure you want to delete this job? This cannot be undone.")),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => confirmDeleteOpen.Set(false)),
                new Button("Delete").Destructive().ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    var id = deleteJobId.Value;
                    if (id != null)
                    {
                        var jobToDelete = jobs.FirstOrDefault(j => j.Id == id);
                        if (jobToDelete?.Status is JobStatus.Running or JobStatus.Queued)
                            jobService.StopJob(id);
                        jobService.DeleteJob(id);
                    }
                    confirmDeleteOpen.Set(false);
                    deleteJobId.Set(null);
                    refreshToken.Refresh();
                })
            )
        ) : null;

        return new Fragment(dataTable, confirmDialog);
    }
}
