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
        try
        {
            MasterClient.PutJson(
                $"api/jobs/{settings.JobId}/fail",
                new { message = settings.Message },
                notFoundMessage: $"Job '{settings.JobId}' is not known to the running Tendril server (it may have restarted, or the job was deleted).",
                cancellationToken: cancellationToken);

            // This only records the failure reason. The promptware is still responsible
            // for exiting non-zero (e.g. `exit 1`) to actually fail the job.
            Console.WriteLine($"Failure reported for job {settings.JobId}");
        }
        catch (Exception ex)
        {
            // Recording the failure reason is best effort. The agent's own non-zero exit
            // is what actually marks the job failed, so a dropped report loses no information.
            Console.Error.WriteLine($"Warning: could not report failure for job {settings.JobId}: {ex.Message}");
        }

        return 0;
    }
}
