using System.ComponentModel;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
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

    [McpServerTool(Name = "tendril_job_status"),
     Description("Update the status message and plan context of a job in the Tendril UI")]
    public string UpdateStatus(
        [Description("Job ID (e.g., '00011')")] string jobId,
        [Description("Status message to display")] string message,
        [Description("Optional plan ID")] string? planId = null,
        [Description("Optional plan title")] string? planTitle = null)
    {
        return ExecuteAuthenticated(() =>
        {
            MasterClient.PutJson(
                $"api/jobs/{jobId}/status",
                new { message, planId, planTitle });
            return $"Status updated for job {jobId}";
        });
    }
}
