using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobStatusSettings : CommandSettings
{
    [Description("Job ID (e.g., job-001)")]
    [CommandArgument(0, "<job-id>")]
    public string JobId { get; set; } = "";

    [Description("Status message to display")]
    [CommandOption("--message|-m")]
    public string Message { get; set; } = "";

    [Description("Plan ID to report")]
    [CommandOption("--plan-id")]
    public string? PlanId { get; set; }

    [Description("Plan title to report")]
    [CommandOption("--plan-title")]
    public string? PlanTitle { get; set; }
}

public class JobStatusCommand : Command<JobStatusSettings>
{
    private readonly ILogger<JobStatusCommand> _logger;

    public JobStatusCommand(ILogger<JobStatusCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, JobStatusSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var statusFile = Environment.GetEnvironmentVariable("TENDRIL_STATUS_FILE");
            if (string.IsNullOrEmpty(statusFile))
                statusFile = JobStatusFile.GetStatusFilePath(settings.JobId);

            JobStatusFile.Write(statusFile, settings.Message, settings.PlanId, settings.PlanTitle);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write job status for {JobId}", settings.JobId);
            return 1;
        }
    }
}
