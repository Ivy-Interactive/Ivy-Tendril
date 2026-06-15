using System.Reflection;
using System.Text;

namespace Ivy.Tendril.Services;

public static class FirmwareCompiler
{
    private static readonly Lazy<string?> PlansReference = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Ivy.Tendril.Prompts.Plans.md");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private const string FirmwareTemplate = """
        ---
        {HEADER}
        ---
        You are an agentic application that evolves over time.

        This prompt is your Firmware and is never allowed to change.

        The header above contains your named parameters for this execution.

        Your program folder is: {PROGRAMFOLDER}

        ## Goal

        Your goal is to complete the instructions in the **Program** section below (inlined from {PROGRAMFOLDER}/Program.md) with the following priority:

        1. Completeness
        2. Speed
        3. Token efficiency
        4. Improvement over time

        **Tools:** 
        {TOOLS}
        
        **Memory:**
        {MEMORY}

        To read a memory file:
        ```bash
        tendril promptware read-memory {PROMPTWARE_NAME} <filename>.md
        ```

        Complete your task and present the user with a summary.

        ## Reflection

        Every execution needs to end with a reflection step. This is your opportunity to improve over time. What did we learn during this session? Save reflections using the CLI:

        **Bash:**
        ```bash
        tendril promptware write-memory {PROMPTWARE_NAME} <filename>.md <<'EOF'
        <reflection content>
        EOF
        ```

        **PowerShell:**
        ```powershell
        @'
        <reflection content>
        '@ | tendril promptware write-memory {PROMPTWARE_NAME} <filename>.md
        ```

        - Note that learnings might be falsified over time. Pruning memory is just as important as storing new memory.
        - Many sessions don't have any new learnings. Only store memory when you need it.
        """;

    public static string Compile(FirmwareContext context)
    {
        var headerValues = new Dictionary<string, string>(context.Values);

        if (!headerValues.ContainsKey("CurrentTime"))
            headerValues["CurrentTime"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var header = string.Join("\n", headerValues
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}: {NormalizeHeaderValue(kv.Key, kv.Value)}"));

        var toolsListing = ListDirectoryFiles(Path.Combine(context.ProgramFolder, "Tools"), "(no tools yet)");
        var memoryListing = ListDirectoryFiles(Path.Combine(context.ProgramFolder, "Memory"), "(no memory yet)");

        var promptwareName = Path.GetFileName(context.ProgramFolder);

        var firmware = FirmwareTemplate
            .Replace("{HEADER}", header)
            .Replace("{PROGRAMFOLDER}", context.ProgramFolder)
            .Replace("{PROMPTWARE_NAME}", promptwareName)
            .Replace("{TOOLS}", toolsListing)
            .Replace("{MEMORY}", memoryListing);

        if (headerValues.TryGetValue("TendrilJobId", out var tendrilJobId) && !string.IsNullOrEmpty(tendrilJobId))
        {
            firmware += $"\n\n**CLI Logging:** Append `--job-id {tendrilJobId}` to every `tendril` CLI command you run " +
                        $"(e.g., `tendril plan add-commit ... --job-id {tendrilJobId}`). This enables execution logging.\n" +
                        $"\n**Status Reporting:** Use `tendril job status {tendrilJobId} --message \"your status\"` to report progress. " +
                        "You can also pass `--plan-id` and `--plan-title` to associate the job with a plan.\n";
        }

        // Include Program.md inline
        var programFile = Path.Combine(context.ProgramFolder, "Program.md");
        if (File.Exists(programFile))
        {
            firmware += "\n\n## Program\n\n";
            firmware += File.ReadAllText(programFile) + "\n";
        }

        if (context.Projects is { Length: > 0 })
        {
            firmware += "\n\n## Projects\n\n";
            firmware += RenderProjects(context.Projects);
        }

        var plansContent = PlansReference.Value;
        if (plansContent != null)
        {
            firmware += "\n\n## Reference Documents\n";
            firmware += $"\n### Plans\n\n{plansContent}\n";
        }

        if (!string.IsNullOrWhiteSpace(context.PlanTemplate))
        {
            firmware += "\n\n## Plan Template\n\n";
            firmware += "Use this template structure when writing plan revisions:\n\n";
            firmware += "```markdown\n" + context.PlanTemplate + "\n```\n";
        }

        if (!string.IsNullOrWhiteSpace(context.CustomInstructions))
        {
            firmware += "\n\n## Custom Instructions\n\n";
            firmware += "IMPORTANT: The following instructions are provided by the user and take precedence over the Firmware template and Program.md instructions. Follow them even if they conflict with other instructions.\n\n";
            firmware += context.CustomInstructions + "\n";
        }

        return firmware;
    }

    private static string RenderProjects(ProjectInfo[] projects)
    {
        var sb = new StringBuilder();

        foreach (var project in projects)
        {
            sb.AppendLine($"### {project.Name}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(project.Context))
            {
                sb.AppendLine(project.Context);
                sb.AppendLine();
            }

            if (project.Repos.Count > 0)
            {
                sb.AppendLine("**Repos:**");
                foreach (var repo in project.Repos)
                    sb.AppendLine($"- {repo.OwnerName} (`{repo.Path}`)");
                sb.AppendLine();
            }

            if (project.Verifications.Count > 0)
            {
                sb.AppendLine("**Verifications:**");
                foreach (var v in project.Verifications)
                {
                    var flag = v.Required ? "required" : "optional";
                    if (v.Delegated) flag += ", delegated";
                    sb.AppendLine($"- {v.Name} ({flag})");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static readonly HashSet<string> PathKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "TendrilHome", "TendrilPlanFolder",
        "TendrilPlansFolder", "SourceUrl", "SourcePath"
    };

    private static string NormalizeHeaderValue(string key, string value) =>
        PathKeys.Contains(key) ? value.Replace('\\', '/') : value;

    private static string ListDirectoryFiles(string directory, string emptyLabel = "(none)")
    {
        if (!Directory.Exists(directory))
            return emptyLabel;

        var files = Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .Where(f => f != null && !f.StartsWith('.'))
            .OrderBy(f => f)
            .ToList();

        return files.Count == 0 ? emptyLabel : string.Join(", ", files);
    }

    public static string GetLogFile(string programFolder, string jobId)
    {
        var logsFolder = Path.Combine(programFolder, "Logs");
        Directory.CreateDirectory(logsFolder);
        var logFile = Path.Combine(logsFolder, $"{jobId}.md");
        File.WriteAllText(logFile, "*Execution in progress...*\n");
        return logFile;
    }
}

public record FirmwareContext(
    string ProgramFolder,
    Dictionary<string, string> Values,
    string? CustomInstructions = null,
    ProjectInfo[]? Projects = null,
    string? PlanTemplate = null);

public record ProjectInfo(
    string Name,
    string Context,
    List<ProjectRepoInfo> Repos,
    List<ProjectVerificationInfo> Verifications);

public record ProjectRepoInfo(
    string Path,
    string OwnerName);

public record ProjectVerificationInfo(
    string Name,
    bool Required,
    bool Delegated);
