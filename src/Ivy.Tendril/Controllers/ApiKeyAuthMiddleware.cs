using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Controllers;

public class ApiKeyAuthMiddleware(RequestDelegate next, IConfigService configService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/api/jobs"))
        {
            var apiKey = configService.Settings.Api?.ApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
                if (string.IsNullOrEmpty(providedKey) || providedKey != apiKey)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
                    return;
                }
            }
        }

        await next(context);
    }
}
