using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private static object BuildDataTable(
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
        List<JobItem> jobs,
        Dictionary<string, string> projectColors,
        StackedProgress jobsProgress)
    {
        return rows.AsQueryable()
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
                    var job = jobs.FirstOrDefault(j => ExtractPlanId(j.PlanFile) == planId);
                    if (job != null && !string.IsNullOrEmpty(job.PlanFile))
                    {
                        var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
                        if (Directory.Exists(fullPath))
                            showPlan(fullPath);
                    }
                }
                return ValueTask.CompletedTask;
            })
            .OnCellAction(t => t.LastOutput, e =>
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
                        var fullPrompt = GetFullPrompt(job, planService);
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

                            var folder = job.TypedArgs.PlanFolder;
                            if (job.TypedArgs is ExecutePlanArgs or ExpandPlanArgs && folder != null)
                            {
                                planService.TransitionState(Path.GetFileName(folder), PlanStatus.Building);
                            }
                            else if (job.TypedArgs is UpdatePlanArgs && folder != null)
                            {
                                planService.TransitionState(Path.GetFileName(folder), PlanStatus.Updating);
                            }

                            jobService.DeleteJob(job.Id);
                            jobService.StartJob(job.TypedArgs);

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
            .HeaderRight(_ => Layout.Horizontal()
                              | jobsProgress
                              | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                                  new MenuItem("Clear Completed", Icon: Icons.Trash, Tag: "ClearCompleted")
                                      .OnSelect(() =>
                                      {
                                          var count = jobService.GetJobs().Count(j => j.Status == JobStatus.Completed);
                                          jobService.ClearCompletedJobs();
                                          refreshToken.Refresh();
                                          client.Toast($"Cleared {count} completed {(count == 1 ? "job" : "jobs")}.", "Clear Completed");
                                      }),
                                  new MenuItem("Clear Failed", Icon: Icons.Trash, Tag: "ClearFailed").OnSelect(() =>
                                  {
                                      var count = jobService.GetJobs().Count(j => j.Status is JobStatus.Failed or JobStatus.Timeout);
                                      jobService.ClearFailedJobs();
                                      refreshToken.Refresh();
                                      client.Toast($"Cleared {count} failed {(count == 1 ? "job" : "jobs")}.", "Clear Failed");
                                  })
                              ));
    }
}
