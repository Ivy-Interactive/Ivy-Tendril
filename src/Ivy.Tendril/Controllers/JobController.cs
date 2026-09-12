using System.Text.Json;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobController(IJobService jobService, IConfigService configService) : ControllerBase
{
    private static string NormalizeJobId(string jobId) => Helpers.JobId.Normalize(jobId);

    [NonAction]
    public Task GetJobEvents(string jobId, CancellationToken cancellationToken) =>
        GetJobEvents(jobId, null, cancellationToken);

    [HttpGet("{jobId}/events")]
    public async Task GetJobEvents(
        string jobId,
        [FromQuery] string[]? kind,
        CancellationToken cancellationToken)
    {
        var id = NormalizeJobId(jobId);
        var job = jobService.GetJob(id);
        if (job == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { error = "Job not found" }, cancellationToken);
            return;
        }

        var allowedKinds = ParseAllowedKinds(kind);

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            foreach (var line in job.OutputLines)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (!MatchesKind(line, allowedKinds)) continue;
                await WriteSseLineAsync(Response, line, cancellationToken);
            }

            if (IsTerminal(job.Status))
            {
                await WriteSseEndAsync(Response, job.Status.ToString(), cancellationToken);
                return;
            }

            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

            void OnJobFinished(JobItem finishedJob)
            {
                if (finishedJob.Id == job.Id)
                {
                    channel.Writer.TryComplete();
                }
            }

            jobService.JobFinished += OnJobFinished;

            using var subscription = job.OutputObservable.Subscribe(
                onNext: line =>
                {
                    if (MatchesKind(line, allowedKinds))
                    {
                        channel.Writer.TryWrite(line);
                    }
                },
                onError: ex => channel.Writer.TryComplete(ex),
                onCompleted: () => channel.Writer.TryComplete());

            try
            {
                if (IsTerminal(job.Status))
                {
                    channel.Writer.TryComplete();
                }

                while (await channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (channel.Reader.TryRead(out var line))
                    {
                        await WriteSseLineAsync(Response, line, cancellationToken);
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await WriteSseEndAsync(Response, job.Status.ToString(), cancellationToken);
                }
            }
            finally
            {
                jobService.JobFinished -= OnJobFinished;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected gracefully
        }
    }

    private static bool IsTerminal(JobStatus status) =>
        status is JobStatus.Completed or JobStatus.Failed or JobStatus.Stopped or JobStatus.Timeout;

    private static async Task WriteSseLineAsync(HttpResponse response, string line, CancellationToken cancellationToken)
    {
        var cleanLine = line.TrimEnd('\r', '\n');
        await response.WriteAsync($"data: {cleanLine}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteSseEndAsync(HttpResponse response, string status, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"event: end\ndata: {{\"status\":\"{status}\"}}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static HashSet<string> ParseAllowedKinds(string[]? kinds)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (kinds == null || kinds.Length == 0) return set;

        foreach (var item in kinds)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            var parts = item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                set.Add(part.ToLowerInvariant());
            }
        }

        if (set.Contains("tool_use"))
        {
            set.Add("tool_call");
            set.Add("tool_result");
        }

        return set;
    }

    private static bool MatchesKind(string line, HashSet<string> allowedKinds)
    {
        if (allowedKinds.Count == 0) return true;

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("kind", out var kindProp))
            {
                var kindStr = kindProp.GetString();
                return kindStr != null && allowedKinds.Contains(kindStr);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    [HttpPost]
    public IActionResult StartJob([FromBody] JobArgsBase args)
    {
        try
        {
            // Plan state transition (and pre-state snapshot) is handled centrally
            // by JobService.StartJob.
            var jobId = jobService.StartJob(args);
            return Ok(new { jobId, status = "Started" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{jobId}")]
    public IActionResult GetJob(string jobId)
    {
        var job = jobService.GetJob(NormalizeJobId(jobId));
        if (job == null)
            return NotFound(new { error = "Job not found" });

        return Ok(new { job.Id, status = job.Status.ToString(), message = job.StatusMessage });
    }

    [HttpPut("{jobId}/status")]
    public IActionResult UpdateJobStatus(string jobId, [FromBody] UpdateJobStatusRequest request)
    {
        if (!jobService.UpdateJobStatus(NormalizeJobId(jobId), request.Message, request.PlanId, request.PlanTitle))
            return NotFound(new { error = "Job not found" });

        return Ok(new { status = "Updated" });
    }

    [HttpPut("{jobId}/fail")]
    public IActionResult ReportJobFailure(string jobId, [FromBody] ReportJobFailureRequest request)
    {
        var id = NormalizeJobId(jobId);
        if (!jobService.ReportJobFailure(id, request.Message))
            return NotFound(new { error = "Job not found" });

        if (request.Stop)
        {
            jobService.StopJob(id);
        }

        return Ok(new { status = "Failure reported" });
    }

    [HttpPost("{jobId}/cancel")]
    public IActionResult CancelJob(string jobId, [FromBody] CancelJobRequest? request)
    {
        var id = NormalizeJobId(jobId);
        var job = jobService.GetJob(id);
        if (job == null)
            return NotFound(new { error = "Job not found" });

        if (!string.IsNullOrWhiteSpace(request?.Message))
        {
            jobService.ReportJobFailure(id, request.Message);
        }

        jobService.StopJob(id);
        return Ok(new { status = "Cancelled" });
    }

    /// <summary>Appends an <c>## Agent Log</c> section to the job's log in <c>&lt;TendrilHome&gt;/Jobs/</c>.</summary>
    [HttpPost("{jobId}/logs")]
    public IActionResult AddLog(string jobId, [FromBody] AddLogRequest request)
    {
        try
        {
            var logPath = JobAddLogCommand.WriteLog(
                configService.TendrilHome, NormalizeJobId(jobId), request.Action, request.Summary);
            return Ok(new { message = $"Log written: {Path.GetFileName(logPath)}" });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", pid = Environment.ProcessId });
}

public record UpdateJobStatusRequest(string Message, string? PlanId = null, string? PlanTitle = null);

public record ReportJobFailureRequest(string Message, bool Stop = false);

public record CancelJobRequest(string? Message = null);

public record AddLogRequest(string Action, string? Summary = null);
