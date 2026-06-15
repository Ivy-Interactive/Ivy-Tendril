using System;
using System.IO;
using System.Threading.Tasks;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/upload")]
public class UploadController(IConfigService configService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded or file is empty." });
        }

        try
        {
            var tempDir = Path.Combine(configService.TendrilHome, "Temp");
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(file.FileName);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var uniqueName = $"{nameWithoutExt}_{Guid.NewGuid().ToString()[..8]}{ext}";
            var filePath = Path.Combine(tempDir, uniqueName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new
            {
                name = file.FileName,
                filePath = filePath
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"File upload failed: {ex.Message}" });
        }
    }
}
