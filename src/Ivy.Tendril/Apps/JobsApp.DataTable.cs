using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private object BuildDataTable(
        List<JobItemRow> rows,
        IRefreshToken refreshToken,
        IObservable<DataTableCellUpdate> updateStream,
        IConfigService config,
        IPlanReaderService planService,
        IJobService jobService,
        IState<string?> showPlan,
        IState<string?> showOutput,
        IState<string?> showPrompt,
        List<JobItem> jobs,
        Dictionary<string, string> projectColors,
        StackedProgress jobsProgress)
    {
        var client = UseService<IClientProvider>();

        return rows.AsQueryable()
            .ToDataTable(t => t.Id)
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
            .Header(t => t.LastOutput, "Last Output")
            .Header(t => t.StatusMessage, "Status")
            .Width(t => t.Status, Size.Px(100))
            .Width(t => t.PlanId, Size.Px(100))
            .Width(t => t.Type, Size.Px(100))
            .Width(t => t.Plan, Size.Px(250))
            .Width(t => t.Project, Size.Px(100))
            .Width(t => t.Timer, Size.Px(100))
            .Width(t => t.LastOutput, Size.Px(100))
            .Width(t => t.Cost, Size.Px(100))
            .Width(t => t.Tokens, Size.Px(100))
            .Width(t => t.StatusMessage, Size.Auto())
            .Renderer(t => t.Status, new LabelsDisplayRenderer
            {
                BadgeColorMapping = StatusMappings.JobStatusColors.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value.ToString()
                )
            })
            .Renderer(t => t.Type, new LabelsDisplayRenderer
            {
                BadgeColorMapping = StatusMappings.JobTypeColors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToString()
                )
            })
            .Renderer(t => t.Project, new LabelsDisplayRenderer
            {
                BadgeColorMapping = projectColors
            })
            .Renderer(t => t.PlanId, new LinkDisplayRenderer())
            .Renderer(t => t.Plan, new TextDisplayRenderer())
            .Renderer(t => t.StatusMessage, new TextDisplayRenderer())
            .Hidden(t => t.Id)
            .Hidden(t => t.LastOutputTimestamp)
            .Hidden(t => t.ErrorContext)
            .SortDirection(t => t.Id, SortDirection.Descending)
            .Filterable(t => t.Timer, false)
            .Filterable(t => t.LastOutput, false)
            .Config(c =>
            {
                c.AllowSorting = true;
                c.AllowFiltering = true;
                c.ShowSearch = false;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
                c.BatchSize = 50;
                c.EnableCellClickEvents = true;
            })
            .OnCellClick(e =>
            {
                if (e.Value.ColumnName == "PlanId")
                {
                    var planId = e.Value.CellValue?.ToString();
                    if (!string.IsNullOrEmpty(planId))
                    {
                        var job = jobs.FirstOrDefault(j => ExtractPlanId(j.PlanFile) == planId);
                        if (job != null && !string.IsNullOrEmpty(job.PlanFile))
                        {
                            var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
                            if (Directory.Exists(fullPath))
                                showPlan.Set(fullPath);
                        }
                    }
                }
                else if (e.Value.ColumnName == "LastOutput")
                {
                    var id = e.Value.RowId?.ToString();
                    if (!string.IsNullOrEmpty(id))
                        showOutput.Set(id);
                }
                else if (e.Value.ColumnName == "StatusMessage")
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
                }

                return ValueTask.CompletedTask;
            })
            .RowActions(row =>
            {
                var job = jobs.FirstOrDefault(j => j.Id == row.Id);
                var actions = new List<MenuItem>
                {
                    new MenuItem("View Plan", Icon: Icons.FileText, Tag: "view-plan").Tooltip("Open the associated plan"),
                    new MenuItem("View Output", Icon: Icons.Terminal, Tag: "view-output").Tooltip(
                        "View job output with Claude JSON rendering"),
                    new MenuItem("Show Prompt", Icon: Icons.MessageSquare, Tag: "show-prompt").Tooltip(
                        "Show the full prompt text"),
                };

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
                    if (tag == "view-plan")
                    {
                        if (!string.IsNullOrEmpty(job.PlanFile))
                        {
                            var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
                            if (Directory.Exists(fullPath))
                                showPlan.Set(fullPath);
                        }
                    }
                    else if (tag == "view-output")
                    {
                        showOutput.Set(job.Id);
                    }
                    else if (tag == "show-prompt")
                    {
                        var fullPrompt = GetFullPrompt(job);
                        if (!string.IsNullOrEmpty(fullPrompt))
                            showPrompt.Set(fullPrompt);
                    }
                    else if (tag == "stop-job")
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
                            if (job.Type == "CreatePlan" && !job.Args.Contains("-Description"))
                            {
                                client.Toast("Cannot rerun CreatePlan: original description was not preserved.", "Rerun Failed");
                                return ValueTask.CompletedTask;
                            }

                            if (job.Type is "ExecutePlan" or "ExpandPlan" && job.Args.Length > 0)
                            {
                                var folderName = Path.GetFileName(job.Args[0]);
                                planService.TransitionState(folderName, PlanStatus.Building);
                            }
                            else if (job is { Type: "UpdatePlan", Args.Length: > 0 })
                            {
                                var folderName = Path.GetFileName(job.Args[0]);
                                planService.TransitionState(folderName, PlanStatus.Updating);
                            }

                            jobService.DeleteJob(job.Id);
                            jobService.StartJob(job.Type, job.Args);

                            refreshToken.Refresh();
                        }
                    }
                    else if (tag == "delete-job")
                    {
                        if (job.Status is JobStatus.Running or JobStatus.Queued)
                        {
                            jobService.StopJob(job.Id);
                        }

                        jobService.DeleteJob(job.Id);
                        refreshToken.Refresh();
                    }
                }

                return ValueTask.CompletedTask;
            })
            .HeaderRight(_ => Layout.Horizontal().Gap(2)
                              | jobsProgress
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
                                  })
                              ));
    }
}
