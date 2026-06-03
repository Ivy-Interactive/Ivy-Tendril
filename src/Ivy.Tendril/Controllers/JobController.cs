using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobController(IJobService jobService, IPlanReaderService planReaderService) : ControllerBase
{
    [HttpPost]
    public IActionResult StartJob([FromBody] JobArgsBase args)
    {
        try
        {
            TransitionPlanState(args);
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
        var job = jobService.GetJob(jobId);
        if (job == null)
            return NotFound(new { error = "Job not found" });

        return Ok(new { job.Id, status = job.Status.ToString(), message = job.StatusMessage });
    }

    [HttpPut("{jobId}/status")]
    public IActionResult UpdateJobStatus(string jobId, [FromBody] UpdateJobStatusRequest request)
    {
        var job = jobService.GetJob(jobId);
        if (job == null)
            return NotFound(new { error = "Job not found" });

        job.StatusMessage = request.Message;
        if (!string.IsNullOrEmpty(request.PlanId))
            job.ReportedPlanId = request.PlanId;
        if (!string.IsNullOrEmpty(request.PlanTitle))
            job.ReportedPlanTitle = request.PlanTitle;

        return Ok(new { status = "Updated" });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", pid = Environment.ProcessId });

    private void TransitionPlanState(JobArgsBase args)
    {
        if (args.PlanFolder == null) return;

        var folderName = Path.GetFileName(args.PlanFolder);

        var targetState = args switch
        {
            ExecutePlanArgs => PlanStatus.Building,
            ExpandPlanArgs => PlanStatus.Building,
            CreatePrArgs => PlanStatus.Building,
            CreateIssueArgs => PlanStatus.Building,
            RetryPlanArgs => PlanStatus.Executing,
            UpdatePlanArgs => PlanStatus.Updating,
            SplitPlanArgs => PlanStatus.Updating,
            _ => (PlanStatus?)null
        };

        if (targetState == null) return;

        if (args is RetryPlanArgs)
            planReaderService.ResetVerificationsForRetry(folderName);

        planReaderService.TransitionState(folderName, targetState.Value);
    }
}

public record UpdateJobStatusRequest(string Message, string? PlanId = null, string? PlanTitle = null);
