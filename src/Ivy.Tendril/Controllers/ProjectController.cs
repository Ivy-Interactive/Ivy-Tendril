using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectController(IConfigService configService) : ControllerBase
{
    [HttpGet]
    public IActionResult GetProjects()
    {
        var projects = configService.Projects.Select(p => new
        {
            name = p.Name,
            color = p.Color,
            repos = p.RepoPaths
        });
        return Ok(projects);
    }
}
