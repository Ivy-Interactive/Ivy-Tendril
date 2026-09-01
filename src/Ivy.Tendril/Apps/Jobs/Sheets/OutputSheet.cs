using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Jobs.Sheets;

public class OutputSheet(string jobId, IJobService jobService) : ViewBase
{
    public override object Build()
    {
        var outputStream = UseStream<string>();
        var initialSnapshot = UseRef<string?>(null);

        var job = jobService.GetJob(jobId);

        initialSnapshot.Value ??= job is { OutputLines.IsEmpty: false }
            ? string.Join("\n", job.OutputLines)
            : null;

        UseEffect(() => job is { Status: JobStatus.Running }
            ? job.OutputObservable.Subscribe(line => outputStream.Write(line))
            : null);

        if (job is null)
            return Text.P("No output available.");

        if (job.OutputLines.IsEmpty && job.Status != JobStatus.Running)
        {
            if (!string.IsNullOrEmpty(job.StatusMessage))
            {
                return Layout.Vertical()
                    .Gap(2)
                    | Callout.Info(job.StatusMessage, $"Job {job.Status}");
            }

            if (job.Status == JobStatus.Blocked)
            {
                return Layout.Vertical()
                    .Gap(2)
                    | Callout.Info("Waiting for dependencies or preceding jobs to complete.", "Job Blocked");
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
