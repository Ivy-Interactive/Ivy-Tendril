using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Jobs;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobAddLogSettings : CommandSettings
{
    [Description("Job ID (e.g., 00458). Agents pass the TendrilJobId firmware header value.")]
    [CommandArgument(0, "<job-id>")]
    public string JobId { get; set; } = "";

    [Description("Action name (e.g., CreatePlan, ExecutePlan)")]
    [CommandArgument(1, "<action>")]
    public string Action { get; set; } = "";

    [CommandOption("--summary")]
    [Description("Optional summary text")]
    public string? Summary { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(JobId, "job-id"),
            CliValidation.RequireNonEmpty(Action, "action"));
    }
}

/// <summary>
/// Appends an agent-authored narrative entry to the job's log in <c>&lt;TendrilHome&gt;/Jobs/</c>.
/// </summary>
public class JobAddLogCommand : Command<JobAddLogSettings>
{
    protected override int Execute(CommandContext context, JobAddLogSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrWhiteSpace(tendrilHome))
        {
            Console.Error.WriteLine("TENDRIL_HOME environment variable is not set");
            return 1;
        }

        try
        {
            var logPath = WriteLog(tendrilHome, settings.JobId, settings.Action, settings.Summary);
            Console.Write(logPath);
            return 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Appends an <c>## Agent Log</c> section to the job's log and returns its path. The section is preceded
    /// by <see cref="JobLogWriter.AgentLogMarker"/> so the completion write can find and preserve it.
    /// </summary>
    internal static string WriteLog(string tendrilHome, string jobId, string action, string? summary = null)
    {
        var logPath = ResolveJobLog(tendrilHome, Helpers.JobId.Normalize(jobId));

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entry = $"\n{JobLogWriter.AgentLogMarker}\n{JobLogWriter.AgentLogHeading}{action} ({now})\n";
        if (!string.IsNullOrEmpty(summary))
            entry += $"\n{summary}\n";

        File.AppendAllText(logPath, entry);
        return logPath;
    }

    /// <summary>
    /// Every launch path seeds the job log before the agent starts (<see cref="JobLogWriter.SeedLog" />),
    /// so a missing log means the job id is wrong rather than that the log is yet to be created.
    /// </summary>
    private static string ResolveJobLog(string tendrilHome, string jobId)
    {
        if (!Helpers.JobId.IsValid(jobId))
            throw new ArgumentException(
                $"Invalid job-id '{jobId}'. Expected an alphanumeric job id such as 00458.");

        var existing = JobLogPaths.AllForJobId(tendrilHome, jobId)
            .FirstOrDefault(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                 && !f.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        throw new FileNotFoundException(
            $"No job log found for job {jobId} in {JobLogPaths.JobsDir(tendrilHome)}.");
    }
}
