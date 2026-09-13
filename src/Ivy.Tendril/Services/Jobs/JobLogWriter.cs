using System.Text;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Jobs;

/// <summary>
/// Writes the Job Log (<c>.md</c>) and the Job Prompt (<c>.prompt.md</c>) into
/// <c>&lt;TendrilHome&gt;/Jobs/</c>. The raw and eventwire streams are appended by <see cref="JobItem"/>
/// itself as output arrives. See <see cref="JobLogPaths"/>.
/// </summary>
/// <remarks>
/// Nothing here mutates the job. <see cref="WriteLog"/> may be called more than once for the same job and
/// will produce the same file each time.
/// </remarks>
public static class JobLogWriter
{
    /// <summary>
    /// Precedes every section appended by <c>tendril job add-log</c>. A comment rather than the rendered
    /// <c>## Agent Log</c> heading, so that an agent echoing that heading in its final output cannot fool
    /// <see cref="ExtractAgentSections"/> into treating the output as an agent section.
    /// </summary>
    public const string AgentLogMarker = "<!-- tendril:agent-log -->";

    public const string AgentLogHeading = "## Agent Log — ";

    /// <summary>
    /// The <see cref="JobItem"/> the standalone CLI runners work with. They must resolve their log paths
    /// before any job record exists, and reuse this same instance for the completion write so the log
    /// records the job's identity. Only Id/Type/PlanFile feed <see cref="JobLogPaths.Stem"/>.
    /// </summary>
    public static JobItem BuildCliRunJob(string jobId, string promptware, IReadOnlyDictionary<string, string> values)
    {
        var planFolder = values.TryGetValue("TendrilPlanFolder", out var pf) ? pf : null;
        return new JobItem
        {
            Id = jobId,
            Type = promptware,
            PlanFile = string.IsNullOrEmpty(planFolder)
                ? ""
                : Path.GetFileName(planFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        };
    }

    /// <summary>Creates the Job Log placeholder so the file exists for the whole run. Returns its path.</summary>
    public static string SeedLog(string tendrilHome, JobItem job)
    {
        JobLogPaths.EnsureJobsDir(tendrilHome);
        var logFile = JobLogPaths.Log(tendrilHome, job);
        FileHelper.WriteAllText(logFile, "*Execution in progress...*\n");
        return logFile;
    }

    /// <summary>
    /// Persists the exact prompt handed to the agent. Written at launch rather than at completion so it
    /// survives a crashed or killed job. Returns the path, or <c>null</c> when there is no prompt.
    /// </summary>
    public static string? WritePrompt(string tendrilHome, JobItem job)
    {
        if (string.IsNullOrEmpty(job.CompiledPrompt)) return null;
        JobLogPaths.EnsureJobsDir(tendrilHome);
        var promptFile = JobLogPaths.Prompt(tendrilHome, job);
        FileHelper.WriteAllText(promptFile, job.CompiledPrompt);
        return promptFile;
    }

    /// <param name="outcomeSection">Optional trailing markdown, e.g. the plan outcome summary.</param>
    /// <param name="fallbackLogPath">
    /// Used when the job never launched and so never had <see cref="JobItem.LogFilePath"/> assigned —
    /// a job that failed its pre-launch guard still deserves a Job Log.
    /// </param>
    public static void WriteLog(JobItem job, string? outcomeSection = null, string? fallbackLogPath = null)
    {
        var logFilePath = job.LogFilePath ?? fallbackLogPath;
        if (string.IsNullOrEmpty(logFilePath)) return;

        var stem = Path.GetFileNameWithoutExtension(logFilePath);
        var agentSections = ExtractAgentSections(logFilePath);

        var sb = new StringBuilder();
        sb.AppendLine($"# Job Log {stem}");
        sb.AppendLine();
        sb.AppendLine($"- **JobId:** {job.Id}");
        // The only place a CreatePlan job records the plan it produced: its stem carries no plan id, so
        // without this line nothing can map that job back to its plan (see BugReportService).
        var planId = job.ResolvePlanId();
        if (!string.IsNullOrEmpty(planId))
            sb.AppendLine($"- **PlanId:** {planId}");
        sb.AppendLine($"- **Status:** {job.Status}");
        sb.AppendLine($"- **Exit Code:** {job.ExitCode?.ToString() ?? "N/A"}");
        sb.AppendLine($"- **Started:** {job.StartedAt:u}");
        sb.AppendLine($"- **Completed:** {job.CompletedAt:u}");
        sb.AppendLine($"- **Duration:** {(job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "unknown")}");
        sb.AppendLine($"- **Provider:** {job.Provider}");
        if (!string.IsNullOrEmpty(job.SessionId))
            sb.AppendLine($"- **SessionId:** {job.SessionId}");
        if (job.Cost.HasValue)
            sb.AppendLine($"- **Cost:** {FormatHelper.FormatCost(job.Cost.Value, decimals: 4)}");
        if (job.Tokens.HasValue)
            sb.AppendLine($"- **Tokens:** {FormatHelper.FormatCount(job.Tokens.Value)}");
        if (job.Status == JobStatus.Timeout && job.StatusMessage != null)
            sb.AppendLine($"- **Timeout Reason:** {job.StatusMessage}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(job.CliCommand))
        {
            sb.AppendLine("## CLI Command");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(job.CliCommand);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        var finalOutput = ExtractFinalOutput(job);
        if (finalOutput != null)
        {
            sb.AppendLine(finalOutput.HeadingSuffix != null
                ? $"## Final Output ({finalOutput.HeadingSuffix})"
                : "## Final Output");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(finalOutput.IncompleteReason))
            {
                sb.AppendLine($"*{finalOutput.IncompleteReason}*");
                sb.AppendLine();
            }
            sb.AppendLine(finalOutput.Text);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(outcomeSection))
        {
            sb.AppendLine(outcomeSection.TrimEnd());
            sb.AppendLine();
        }

        // Agent sections go last, so re-running WriteLog over its own output reproduces this same file.
        if (!string.IsNullOrEmpty(agentSections))
            sb.Append(agentSections);

        FileHelper.WriteAllText(logFilePath, sb.ToString());
    }

    /// <summary>
    /// Reads back the sections a running agent appended via <c>job add-log</c>, from the first marker to the
    /// end of the file. Without this, the completion write would truncate them.
    /// </summary>
    private static string? ExtractAgentSections(string logFilePath)
    {
        try
        {
            if (!File.Exists(logFilePath)) return null;
            var text = FileHelper.ReadAllText(logFilePath);
            var idx = text.IndexOf(AgentLogMarker, StringComparison.Ordinal);
            return idx < 0 ? null : text[idx..];
        }
        catch
        {
            return null;
        }
    }

    /// <param name="Text">The assistant's final response, or its partial text when truncated/incomplete.</param>
    /// <param name="IncompleteReason">The flagged <see cref="ErrorEvent.Message"/>, rendered as an italic note.</param>
    /// <param name="HeadingSuffix">Appended to the <c>## Final Output</c> heading in parentheses, e.g. <c>truncated</c>.</param>
    private sealed record FinalOutput(string Text, string? IncompleteReason, string? HeadingSuffix);

    /// <summary>
    /// Only <see cref="ResultEvent.Response"/> counts as today's "final output". When a run ends without one
    /// (a truncation or unhandled stop reason flagged by <see cref="OpenCodeEventParser"/>), fall back to the
    /// assistant's partial <see cref="TextEvent"/> text instead of discarding it — but only for those flagged
    /// codes, so every other job log stays byte-identical to before.
    /// </summary>
    private static FinalOutput? ExtractFinalOutput(JobItem job)
    {
        if (job.OutputLines.Count == 0) return null;

        try
        {
            var serializer = new JsonEventSerializer();
            string? lastResponse = null;
            string? partialText = null;
            string? incompleteReason = null;
            string? headingSuffix = null;

            foreach (var line in job.OutputLines)
            {
                switch (serializer.Deserialize(line))
                {
                    case ResultEvent { Response: { } response }:
                        lastResponse = response;
                        break;

                    // Skip Tendril's own synthetic completion/hook lines — they're enqueued as TextEvents
                    // too (JobItem.EnqueueSystemOutput) and would otherwise look like the model's own text.
                    case TextEvent text when text.Text.StartsWith("[Tendril] ", StringComparison.Ordinal)
                        || text.Text.StartsWith("[hook:", StringComparison.Ordinal):
                        break;

                    case TextEvent text:
                        partialText = text.IsDelta ? (partialText ?? "") + text.Text : text.Text;
                        break;

                    case ErrorEvent { Code: OpenCodeEventParser.OutputTruncatedCode } error:
                        incompleteReason = error.Message;
                        headingSuffix = "truncated";
                        break;

                    case ErrorEvent { Code: OpenCodeEventParser.UnhandledStopReasonCode } error:
                        incompleteReason = error.Message;
                        headingSuffix = "incomplete";
                        break;
                }
            }

            if (!string.IsNullOrEmpty(lastResponse))
                return new FinalOutput(lastResponse, headingSuffix != null ? incompleteReason : null, headingSuffix);

            if (headingSuffix != null && !string.IsNullOrEmpty(partialText))
                return new FinalOutput(partialText, incompleteReason, headingSuffix);

            return null;
        }
        catch
        {
            return null;
        }
    }
}
