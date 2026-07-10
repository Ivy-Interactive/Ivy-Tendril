using System.ComponentModel;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using ModelContextProtocol.Server;

namespace Ivy.Tendril.Mcp.Tools;

[McpServerToolType]
public sealed class JobTools : AuthenticatedToolBase
{
    private readonly IConfigService _configService;

    public JobTools(McpAuthenticationService authService, IConfigService configService) : base(authService)
    {
        _configService = configService;
    }

    [McpServerTool(Name = "tendril_job_add_log"),
     Description("Append a narrative log entry to a job's log in <TendrilHome>/Jobs/")]
    public string AddLog(
        [Description("Job ID (e.g., '00458')")] string jobId,
        [Description("Action name (e.g., CreatePlan, ExecutePlan)")] string action,
        [Description("Optional summary text")] string? summary = null)
    {
        return ExecuteAuthenticated(() =>
        {
            var logPath = JobAddLogCommand.WriteLog(_configService.TendrilHome, jobId, action, summary);
            return $"Log written: {Path.GetFileName(logPath)}";
        });
    }
}
