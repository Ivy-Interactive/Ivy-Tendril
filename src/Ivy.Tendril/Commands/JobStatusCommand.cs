using System.ComponentModel;
using Ivy.Tendril.Helpers;
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(JobId, "job-id");
    }
}

public class JobStatusCommand : Command<JobStatusSettings>
{
    protected override int Execute(CommandContext context, JobStatusSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            MasterClient.PutJson(
                $"api/jobs/{settings.JobId}/status",
                new { message = settings.Message, planId = settings.PlanId, planTitle = settings.PlanTitle },
                notFoundMessage: $"Job '{settings.JobId}' is not known to the running Tendril server (it may have restarted, or the job was deleted).",
                cancellationToken: cancellationToken);

            Console.WriteLine($"Status updated for job {settings.JobId}");
        }
        catch (Exception ex)
        {
            // Status reporting is telemetry for the Jobs UI. A failed report must not fail
            // the agent's script, which is what actually determines job success/failure.
            Console.Error.WriteLine($"Warning: could not report status for job {settings.JobId}: {ex.Message}");
        }

        return 0;
    }
}
