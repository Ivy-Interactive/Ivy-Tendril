using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Widgets.AgentOutputView;

namespace Ivy.Tendril.Apps.Jobs;

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

        if (job is null || (job.OutputLines.IsEmpty && job.Status != JobStatus.Running))
            return Text.P("No output available.");

        if (job.Status != JobStatus.Running)
        {
            var snapshot = !job.OutputLines.IsEmpty
                ? string.Join("\n", job.OutputLines) : null;

            return new AgentOutputView()
                .Provider(job.Provider)
                .JsonStream(snapshot)
                .AutoScroll(false)
                .ShowStatusLabel(false)
                .Height(Size.Full());
        }

        return new AgentOutputView()
            .Provider(job.Provider)
            .JsonStream(initialSnapshot.Value)
            .Stream(outputStream)
            .Height(Size.Full());
    }
}
