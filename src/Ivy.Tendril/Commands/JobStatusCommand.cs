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
        // Progress telemetry must never fail an agent run: warn and still exit 0.
        var (ok, error) = MasterClient.TryPutJson(
            $"api/jobs/{settings.JobId}/status",
            new { message = settings.Message, planId = settings.PlanId, planTitle = settings.PlanTitle },
            cancellationToken);

        if (!ok)
        {
            Console.Error.WriteLine($"Warning: could not report status for job {settings.JobId}: {error}");
            return 0;
        }

        Console.WriteLine($"Status updated for job {settings.JobId}");
        return 0;
    }
}
