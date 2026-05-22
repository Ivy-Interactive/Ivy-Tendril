using System.Text;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Agents;

public static class PromptwareLogWriter
{
    public static void WriteLog(JobItem job)
    {
        if (string.IsNullOrEmpty(job.LogFilePath)) return;

        var logNumber = Path.GetFileNameWithoutExtension(job.LogFilePath);
        var sb = new StringBuilder();
        sb.AppendLine($"# Execution Log {logNumber}");
        sb.AppendLine();
        sb.AppendLine($"- **Status:** {job.Status}");
        sb.AppendLine($"- **Exit Code:** {job.ExitCode?.ToString() ?? "N/A"}");
        sb.AppendLine($"- **Started:** {job.StartedAt:u}");
        sb.AppendLine($"- **Completed:** {job.CompletedAt:u}");
        sb.AppendLine($"- **Duration:** {(job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "unknown")}");
        sb.AppendLine($"- **Provider:** {job.Provider}");
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

        if (!string.IsNullOrEmpty(job.CompiledPrompt))
        {
            sb.AppendLine("## Compiled Prompt");
            sb.AppendLine();
            sb.AppendLine("````markdown");
            sb.AppendLine(job.CompiledPrompt);
            sb.AppendLine("````");
            sb.AppendLine();
        }

        var result = ExtractFinalOutput(job);
        if (!string.IsNullOrEmpty(result))
        {
            sb.AppendLine("## Final Output");
            sb.AppendLine();
            sb.AppendLine(result);
            sb.AppendLine();
        }

        File.WriteAllText(job.LogFilePath, sb.ToString());

        job.CompiledPrompt = null;
        job.CliCommand = null;
    }

    public static void WriteRawLog(string logFilePath, IEnumerable<string> outputLines)
    {
        var rawFile = Path.ChangeExtension(logFilePath, ".raw.jsonl");
        if (File.Exists(rawFile) && new FileInfo(rawFile).Length > 0)
            return;
        File.WriteAllLines(rawFile, outputLines);
    }

    private static string? ExtractFinalOutput(JobItem job)
    {
        if (job.OutputLines.Count == 0) return null;

        try
        {
            var serializer = new Ivy.Tendril.Agents.Runtime.JsonEventSerializer();
            string? lastResult = null;
            foreach (var line in job.OutputLines)
            {
                var evt = serializer.Deserialize(line);
                if (evt is Ivy.Tendril.Agents.Abstractions.ResultEvent r && r.Response != null)
                    lastResult = r.Response;
            }
            return lastResult;
        }
        catch
        {
            return null;
        }
    }
}
