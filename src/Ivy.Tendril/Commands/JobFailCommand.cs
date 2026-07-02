using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobFailSettings : CommandSettings
{
    [Description("Job ID (e.g., job-001)")]
    [CommandArgument(0, "<job-id>")]
    public string JobId { get; set; } = "";

    [Description("Descriptive failure message (what failed and why)")]
    [CommandOption("--message|-m")]
    public string Message { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        var jobIdResult = CliValidation.RequireNonEmpty(JobId, "job-id");
        return jobIdResult.Successful
            ? CliValidation.RequireNonEmpty(Message, "message")
            : jobIdResult;
    }
}

public class JobFailCommand : Command<JobFailSettings>
{
    protected override int Execute(CommandContext context, JobFailSettings settings, CancellationToken cancellationToken)
    {
        MasterClient.PutJson($"api/jobs/{settings.JobId}/fail", new { message = settings.Message }, cancellationToken);

        // This only records the failure reason. The promptware is still responsible
        // for exiting non-zero (e.g. `exit 1`) to actually fail the job.
        Console.WriteLine($"Failure reported for job {settings.JobId}");
        return 0;
    }
}
