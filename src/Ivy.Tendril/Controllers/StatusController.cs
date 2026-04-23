using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/jobs")]
public class StatusController(IJobService jobService) : ControllerBase
{
    [HttpPost("{jobId}/status")]
    public IActionResult PostStatus(string jobId, [FromBody] StatusRequest request)
    {
        var job = jobService.GetJob(jobId);
        if (job == null) return NotFound();
        job.StatusMessage = request.Message;
        if (!string.IsNullOrEmpty(request.PlanId))
            job.ReportedPlanId = request.PlanId;
        if (!string.IsNullOrEmpty(request.PlanTitle))
            job.ReportedPlanTitle = request.PlanTitle;
        return Ok();
    }
}

public record StatusRequest(string Message, string? PlanId = null, string? PlanTitle = null);