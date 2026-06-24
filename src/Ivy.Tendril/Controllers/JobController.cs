using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobController(IJobService jobService) : ControllerBase
{
    private static string NormalizeJobId(string jobId) =>
        int.TryParse(jobId, out var num) ? num.ToString("D5") : jobId;

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

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", pid = Environment.ProcessId });
}

public record UpdateJobStatusRequest(string Message, string? PlanId = null, string? PlanTitle = null);
