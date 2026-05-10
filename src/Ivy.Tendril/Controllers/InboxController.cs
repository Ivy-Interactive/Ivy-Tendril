using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/inbox")]
public class InboxController(IJobService jobService) : ControllerBase
{
    [HttpPost]
    public IActionResult PostPlan([FromBody] CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { error = "Description is required" });

        try
        {
            var project = request.Project ?? "Auto";
            var args = new CreatePlanArgs(request.Description, project, SourcePath: request.SourcePath);
            var jobId = jobService.StartJob(Constants.JobTypes.CreatePlan, args);
            return Ok(new { jobId, status = "Started", message = "Plan creation job started successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Failed to start plan creation: {ex.Message}" });
        }
    }
}

public record CreatePlanRequest(
    string Description,
    string? Project = null,
    string? SourcePath = null
);
