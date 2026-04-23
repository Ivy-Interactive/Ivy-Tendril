using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Mcp;

public class TendrilMcpServer
{
    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            var builder = Host.CreateEmptyApplicationBuilder(settings: null);

            builder.Services.AddLogging(logging => logging.AddConsole());

            // Register authentication service
            builder.Services.AddSingleton<McpAuthenticationService>();

            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "tendril",
                        Version = typeof(TendrilMcpServer).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                    };
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();
            var logger = host.Services.GetRequiredService<ILogger<TendrilMcpServer>>();

            // Validate authentication on startup
            var authService = host.Services.GetRequiredService<McpAuthenticationService>();
            if (!authService.ValidateEnvironmentToken())
            {
                logger.LogError("MCP server authentication failed. Ensure TENDRIL_MCP_TOKEN is set correctly");
                return 1;
            }

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            // Logger may not be available if host build itself failed; fall back is acceptable
            Console.Error.WriteLine($"MCP server error: {ex.Message}");
            return 1;
        }
    }
}
