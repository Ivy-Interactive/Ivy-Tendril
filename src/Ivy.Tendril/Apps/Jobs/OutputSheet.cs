using Ivy.Tendril.Services;
using Ivy.Widgets.ClaudeJsonRenderer;

namespace Ivy.Tendril.Apps.Jobs;

public class OutputSheet(
    string jobId,
    IJobService jobService,
    IWriteStream<string> outputStream,
    IState<bool> hasStreamContent) : ViewBase
{
    private readonly string _jobId = jobId;
    private readonly IJobService _jobService = jobService;
    private readonly IWriteStream<string> _outputStream = outputStream;
    private readonly IState<bool> _hasStreamContent = hasStreamContent;

    public override object Build()
    {
        var job = _jobService.GetJob(_jobId);
        object outputContent;

        if (job is { Status: JobStatus.Running })
        {
            if (!_hasStreamContent.Value)
            {
                outputContent = Text.P("Loading Output...");
            }
            else
            {
                outputContent = new ClaudeJsonRenderer()
                    .Stream(_outputStream)
                    .ShowThinking(true)
                    .ShowSystemEvents(false)
                    .AutoScroll(true)
                    .Height(Size.Full());
            }
        }
        else if (job is not null && job.OutputLines.Count > 0)
        {
            var jsonStream = string.Join("\n", job.OutputLines);
            outputContent = new ClaudeJsonRenderer()
                .JsonStream(jsonStream)
                .ShowThinking(true)
                .ShowSystemEvents(true)
                .AutoScroll(false)
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
        var job = _jobService.GetJob(_jobId);
        return job is not null ? $"{job.Type} — {ExtractPlanId(job.PlanFile)}" : "Job Output";
    }

    private static string ExtractPlanId(string planFile)
    {
        if (string.IsNullOrEmpty(planFile)) return "";
        var match = System.Text.RegularExpressions.Regex.Match(planFile, @"^(\d{5})-");
        return match.Success ? match.Groups[1].Value : "";
    }
}
