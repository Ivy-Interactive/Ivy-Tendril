using Ivy.Tendril.Apps.Jobs.Helpers;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Jobs.Sheets;

public class OutputSheet(string jobId, IJobService jobService) : ViewBase
{
    public override object Build()
    {
        var outputStream = UseStream<string>();
        var initialSnapshot = UseRef<string?>(null);
        var nav = UseNavigation();
        var planService = UseService<IPlanReaderService>();

        var job = jobService.GetJob(jobId);

        initialSnapshot.Value ??= job is { OutputLines.IsEmpty: false }
            ? string.Join("\n", job.OutputLines)
            : null;

        UseEffect(() => job is { Status: JobStatus.Running }
            ? job.OutputObservable.Subscribe(line => outputStream.Write(line))
            : null);

        if (job is null)
            return Text.P("No output available.");

        if (job.Status == JobStatus.Blocked)
        {
            var blockingDeps = JobDependencyHelper.GetBlockingDependencies(job, jobService, planService);

            var message = !string.IsNullOrEmpty(job.StatusMessage)
                ? job.StatusMessage
                : "Waiting for dependencies or preceding jobs to complete.";

            var layout = Layout.Vertical().Gap(3)
                         | Callout.Info(message, "Job Blocked");

            if (blockingDeps.Count > 0)
            {
                var buttons = Layout.Horizontal().Gap(2);
                foreach (var dep in blockingDeps)
                {
                    if (!string.IsNullOrEmpty(dep.JobId))
                    {
                        var label = dep.JobStatus.HasValue && !string.IsNullOrEmpty(dep.JobType)
                            ? $"View Job {dep.JobId} ({dep.JobType} - {dep.JobStatus})"
                            : $"View Job {dep.JobId}";
                        buttons |= new Button(label)
                            .Icon(Icons.ArrowRight)
                            .Primary()
                            .OnClick(() => nav.Navigate<JobsApp>(new JobsAppArgs(dep.JobId)));
                    }
                    else if (!string.IsNullOrEmpty(dep.PlanId) || !string.IsNullOrEmpty(dep.PlanFolder))
                    {
                        var planTarget = dep.PlanFolder ?? dep.PlanId!;
                        var planDisplay = dep.PlanId ?? dep.PlanFolder;
                        buttons |= new Button($"View Plan {planDisplay}")
                            .Icon(Icons.ArrowRight)
                            .Outline()
                            .OnClick(() => nav.Navigate<PlansApp>(new PlansAppArgs(planTarget)));
                    }
                }
                layout |= buttons;
            }

            return layout;
        }

        if (job.OutputLines.IsEmpty && job.Status != JobStatus.Running)
        {
            if (!string.IsNullOrEmpty(job.StatusMessage))
            {
                return Layout.Vertical()
                    .Gap(2)
                    | Callout.Info(job.StatusMessage, $"Job {job.Status}");
            }

            if (job.Status == JobStatus.Pending)
            {
                return Layout.Vertical()
                    .Gap(2)
                    | Callout.Info("Job is queued and waiting to start.", "Job Pending");
            }

            return Text.P("No output available.");
        }

        if (job.Status != JobStatus.Running)
        {
            var snapshot = !job.OutputLines.IsEmpty
                ? string.Join("\n", job.OutputLines) : null;

            return new AgentViewer()
                .JsonStream(snapshot)
                .AutoScroll(false)
                .ShowStatusLabel(false)
                .Height(Size.Full());
        }

        return new AgentViewer()
            .JsonStream(initialSnapshot.Value)
            .Stream(outputStream)
            .Height(Size.Full());
    }
}
