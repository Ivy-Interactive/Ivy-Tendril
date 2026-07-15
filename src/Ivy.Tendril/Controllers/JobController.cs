using Ivy.Tendril.Commands;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobController(IJobService jobService, IConfigService configService) : ControllerBase
{
    private static string NormalizeJobId(string jobId) => Helpers.JobId.Normalize(jobId);

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
        if (!jobService.ReportJobFailure(NormalizeJobId(jobId), request.Message))
            return NotFound(new { error = "Job not found" });

        return Ok(new { status = "Failure reported" });
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

public record ReportJobFailureRequest(string Message);

public record AddLogRequest(string Action, string? Summary = null);
