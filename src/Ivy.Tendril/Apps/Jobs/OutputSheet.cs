using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Widgets.AgentOutputView;

namespace Ivy.Tendril.Apps.Jobs;

public partial class OutputSheet(
    string jobId,
    IJobService jobService,
    IWriteStream<string> outputStream,
    IState<bool> hasStreamContent,
    IState<string?> streamingJobId) : ViewBase
{
    public override object Build()
    {
        var job = jobService.GetJob(jobId);
        object agentOutputView;

        if (job is { Status: JobStatus.Running })
        {
            var initialContent = job.OutputLines.IsEmpty ? null : string.Join("\n", job.OutputLines);
            agentOutputView = new AgentOutputView()
                .Provider(job.Provider)
                .JsonStream(initialContent)
                .Stream(outputStream)
                .Height(Size.Full());
        }
        else if (job is not null && hasStreamContent.Value && streamingJobId.Value == jobId)
        {
            var initialContent = job.OutputLines.IsEmpty ? null : string.Join("\n", job.OutputLines);
            agentOutputView = new AgentOutputView()
                .Provider(job.Provider)
                .JsonStream(initialContent)
                .Stream(outputStream)
                .AutoScroll(false)
                .Height(Size.Full());
        }
        else if (job is not null && !job.OutputLines.IsEmpty)
        {
            var jsonStream = string.Join("\n", job.OutputLines);
            agentOutputView = new AgentOutputView()
                .Provider(job.Provider)
                .JsonStream(jsonStream)
                .AutoScroll(false)
                .ShowStatusLabel(false)
                .Height(Size.Full());
        }
        else
        {
            agentOutputView = Text.P("No output available.");
        }

        return agentOutputView;
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
