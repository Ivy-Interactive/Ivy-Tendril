using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/news")]
public class NewsController : ControllerBase
{
    private static readonly HttpClient Http = new();

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            var json = await Http.GetStringAsync(Constants.NewsUrl);
            return Content(json, "application/json");
        }
        catch
        {
            return Ok("[]");
        }
    }
}
