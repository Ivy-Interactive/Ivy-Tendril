using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Widgets.AgentOutputView;

namespace Ivy.Tendril.Apps.Jobs;

public class OutputSheet(
    string jobId,
    IJobService jobService,
    IWriteStream<string> outputStream,
    IState<bool> hasStreamContent,
    IState<string?> streamingJobId) : ViewBase
{
    public override object Build()
    {
        var job = jobService.GetJob(jobId);
        object outputContent;

        if (job is { Status: JobStatus.Running })
        {
            outputContent = new AgentOutputView()
                .Provider(job.Provider)
                .Stream(outputStream)
                .AutoScroll(true)
                .ShowStatusLabel(true)
                .Height(Size.Full());
        }
        else if (job is not null && hasStreamContent.Value && streamingJobId.Value == jobId)
        {
            outputContent = new AgentOutputView()
                .Provider(job.Provider)
                .Stream(outputStream)
                .AutoScroll(false)
                .ShowStatusLabel(true)
                .Height(Size.Full());
        }
        else if (job is not null && job.OutputLines.Count > 0)
        {
            var jsonStream = string.Join("\n", job.OutputLines);
            outputContent = new AgentOutputView()
                .Provider(job.Provider)
                .JsonStream(jsonStream)
                .AutoScroll(false)
                .ShowStatusLabel(false)
                .Height(Size.Full());
        }
        else
        {
            outputContent = Text.P("No output available.");
        }

        return outputContent;
    }

    public string GetSheetTitle()
    {
        var job = jobService.GetJob(jobId);
        return job is not null ? $"{job.Type} {ExtractPlanId(job.PlanFile)}" : "Job Output";
    }

    private static string ExtractPlanId(string planFile)
    {
        if (string.IsNullOrEmpty(planFile)) return "";
        var match = System.Text.RegularExpressions.Regex.Match(planFile, @"^(\d{5})-");
        return match.Success ? match.Groups[1].Value : "";
    }
}
