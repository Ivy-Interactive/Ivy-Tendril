using System.Text.RegularExpressions;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Resolves the four on-disk artifacts every job produces, all of which live flat in
/// <c>&lt;TendrilHome&gt;/Jobs/</c> and share a single stem:
/// <list type="bullet">
///   <item><c>{stem}.md</c> — the Job Log</item>
///   <item><c>{stem}.prompt.md</c> — the Job Prompt</item>
///   <item><c>{stem}.raw.jsonl</c> — the Job Raw Log (unparsed CLI output)</item>
///   <item><c>{stem}.eventwire.jsonl</c> — the Job Eventwire Log</item>
/// </list>
/// Path resolution is pure. Only <see cref="EnsureJobsDir" /> touches the filesystem, and only writers
/// call it — resolving a path must never create a directory, least of all a relative <c>Jobs/</c> in the
/// working directory when <c>TendrilHome</c> is unset.
/// </summary>
public static partial class JobLogPaths
{
    [GeneratedRegex(@"^(\d{5})-")]
    private static partial Regex PlanIdPrefix();

    /// <summary>The one job type whose <see cref="JobItem.PlanFile"/> is rewritten after the run.</summary>
    private const string CreatePlanType = "CreatePlan";

    /// <summary>
    /// The 5-digit plan id prefixing a plan folder name, or <c>null</c>. CreatePlan jobs hold a
    /// task description in <c>PlanFile</c> rather than a folder name, so they yield <c>null</c>.
    /// </summary>
    public static string? PlanIdFromFolderName(string? planFolderName)
    {
        if (string.IsNullOrEmpty(planFolderName)) return null;
        var match = PlanIdPrefix().Match(planFolderName);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// <c>{jobId}-{planId}-{type}</c>, or <c>{jobId}-{type}</c> when the job is not tied to a plan.
    /// </summary>
    /// <remarks>
    /// The stem must be constant for a job's whole lifetime, or a job's artifacts would scatter across two
    /// names. Hence two rules. It derives from <paramref name="planFile" /> alone, never from the
    /// allocated/reported plan id, which a CreatePlan job only acquires mid-run. And a CreatePlan job never
    /// gets a plan-id segment at all: it starts with the task description in <c>PlanFile</c> and
    /// <c>JobCompletionHandler.VerifyCreatePlanResult</c> overwrites that with the folder it produced.
    /// </remarks>
    public static string Stem(string jobId, string type, string? planFile)
    {
        var planId = type == CreatePlanType ? null : PlanIdFromFolderName(planFile);
        return planId is null ? $"{jobId}-{type}" : $"{jobId}-{planId}-{type}";
    }

    public static string Stem(JobItem job) => Stem(job.Id, job.Type, job.PlanFile);

    /// <summary>The Jobs directory path. Pure — see <see cref="EnsureJobsDir" /> to create it.</summary>
    public static string JobsDir(string tendrilHome)
    {
        if (string.IsNullOrWhiteSpace(tendrilHome))
            throw new ArgumentException("TendrilHome is not set; cannot resolve the Jobs directory.", nameof(tendrilHome));
        return Path.Combine(tendrilHome, "Jobs");
    }

    /// <summary>Creates the Jobs directory and returns it. Writers only.</summary>
    public static string EnsureJobsDir(string tendrilHome)
    {
        var dir = JobsDir(tendrilHome);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Log(string tendrilHome, JobItem job) => Path.Combine(JobsDir(tendrilHome), Stem(job) + ".md");
    public static string Prompt(string tendrilHome, JobItem job) => Path.Combine(JobsDir(tendrilHome), Stem(job) + ".prompt.md");
    public static string Raw(string tendrilHome, JobItem job) => Path.Combine(JobsDir(tendrilHome), Stem(job) + ".raw.jsonl");
    public static string EventWire(string tendrilHome, JobItem job) => Path.Combine(JobsDir(tendrilHome), Stem(job) + ".eventwire.jsonl");

    /// <summary>Every artifact belonging to <paramref name="jobId"/>, for bug reports.</summary>
    public static string[] AllForJobId(string tendrilHome, string jobId)
    {
        try
        {
            var dir = JobsDir(tendrilHome);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, $"{jobId}-*") : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Job logs (<c>.md</c>) belonging to <paramref name="planId"/>, oldest job id first.</summary>
    public static string[] LogsForPlanId(string tendrilHome, string planId)
    {
        try
        {
            var dir = JobsDir(tendrilHome);
            if (!Directory.Exists(dir)) return [];
            return Directory.GetFiles(dir, $"*-{planId}-*.md")
                .Where(f => !f.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
