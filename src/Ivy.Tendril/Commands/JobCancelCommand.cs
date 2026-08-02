using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobCancelSettings : CommandSettings
{
    [Description("Job ID (e.g., 00011 or job-0011)")]
    [CommandArgument(0, "<job-id>")]
    public string JobId { get; set; } = "";

    [Description("Reason for cancellation")]
    [CommandOption("--message|-m")]
    public string? Message { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(JobId, "job-id");
    }
}

public class JobCancelCommand : Command<JobCancelSettings>
{
    protected override int Execute(CommandContext context, JobCancelSettings settings, CancellationToken cancellationToken)
    {
        var (ok, error) = MasterClient.TryPostJson(
            $"api/jobs/{settings.JobId}/cancel",
            new { message = settings.Message ?? "Cancelled by user" },
            cancellationToken);

        if (!ok)
        {
            Console.Error.WriteLine($"Error: could not cancel job {settings.JobId}: {error}");
            return 1;
        }

        Console.WriteLine($"Cancelled job {settings.JobId}");
        return 0;
    }
}
