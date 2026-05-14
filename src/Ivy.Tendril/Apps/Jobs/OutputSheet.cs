using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Widgets.AgentOutputView;

namespace Ivy.Tendril.Apps.Jobs;

public partial class OutputSheet(string jobId, IJobService jobService) : ViewBase
{
    public override object Build()
    {
        var outputStream = UseStream<string>();
        var job = jobService.GetJob(jobId);

        UseEffect(() => job is { Status: JobStatus.Running }
            ? job.OutputObservable.Subscribe(line => outputStream.Write(line))
            : null);

        if (job is null || (job.OutputLines.IsEmpty && job.Status != JobStatus.Running))
            return Text.P("No output available.");

        var snapshot = !job.OutputLines.IsEmpty
            ? string.Join("\n", job.OutputLines) : null;

        if (job.Status != JobStatus.Running)
        {
            return new AgentOutputView()
                .Provider(job.Provider)
                .JsonStream(snapshot)
                .AutoScroll(false)
                .ShowStatusLabel(false)
                .Height(Size.Full());
        }

        return new AgentOutputView()
            .Provider(job.Provider)
            .JsonStream(snapshot)
            .Stream(outputStream)
            .Height(Size.Full());
    }

    public string GetSheetTitle()
    {
        var job = jobService.GetJob(jobId);
        return job is not null ? $"{job.Type} {ExtractPlanId(job.PlanFile)}" : "Job Output";
    }

    private static string ExtractPlanId(string planFile)
    {
        if (string.IsNullOrEmpty(planFile)) return "";
        var match = ExtractPlanIdRegex().Match(planFile);
        return match.Success ? match.Groups[1].Value : "";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(\d{5})-")]
    private static partial System.Text.RegularExpressions.Regex ExtractPlanIdRegex();
}
