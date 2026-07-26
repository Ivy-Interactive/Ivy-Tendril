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

        A `[[name]]` cross-reference inside a memory means the file `name.md`. If a read fails, the memory was likely pruned: run `tendril promptware list-memory {PROMPTWARE_NAME}` for the current list instead of guessing filenames.

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

        To delete a memory file that is no longer true:
        ```bash
        tendril promptware delete-memory {PROMPTWARE_NAME} <filename>.md
        ```

        **Memory hygiene (do this before writing any new memory):**
        - Read the listed memories that overlap what you touched this session. A memory you did not verify is a memory you cannot trust.
        - If this session proved a memory wrong (a command it calls broken now works, a workaround it prescribes is no longer needed, a path or flag it names no longer exists), delete it with `delete-memory`. Do not keep it for history.
        - Never write a memory saying that something is fixed or works now. Such a memory is only meaningful against the memory that said it was broken, so delete that one instead of adding a second file.
        - If a memory is only partly stale, rewrite the whole file with `write-memory` under the same filename (it overwrites) instead of adding a near duplicate.
        - Learnings get falsified over time. Pruning memory is as important as storing it.
        - Many sessions have no new learnings. Only store memory when you need it.
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
        var memoryListing = ListMemoryFiles(Path.Combine(context.ProgramFolder, "Memory"));

        var promptwareName = Path.GetFileName(context.ProgramFolder);

        var firmware = FirmwareTemplate
            .Replace("{HEADER}", header)
            .Replace("{PROGRAMFOLDER}", context.ProgramFolder)
            .Replace("{PROMPTWARE_NAME}", promptwareName)
            .Replace("{TOOLS}", toolsListing)
            .Replace("{MEMORY}", memoryListing);

        if (headerValues.TryGetValue("TendrilJobId", out var tendrilJobId) && !string.IsNullOrEmpty(tendrilJobId))
        {
            firmware += $"\n\n**Status Reporting:** Use `tendril job status {tendrilJobId} --message \"your status\"` to report progress. " +
                        "You can also pass `--plan-id` and `--plan-title` to associate the job with a plan.\n";
            firmware += $"\n**Failure Reporting:** On any unrecoverable failure, call `tendril job fail {tendrilJobId} --message \"<what failed and why>\"` before you `exit 1`, " +
                        "so the failure reason is reported cleanly instead of being guessed from your output.\n";
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

    private static string NormalizeHeaderValue(string key, string value)
    {
        if (PathKeys.Contains(key))
            value = value.Replace('\\', '/');

        // Multi-line values use YAML literal block scalar so they don't break subsequent key: value pairs
        if (value.Contains('\n'))
            return "|\n  " + value.TrimEnd().Replace("\n", "\n  ");

        return value;
    }

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

    private static string ListMemoryFiles(string directory, string emptyLabel = "(no memory yet)")
    {
        if (!Directory.Exists(directory))
            return emptyLabel;

        var files = Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .Where(f => f != null && !f.StartsWith('.'))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            return emptyLabel;

        var lines = files.Select(f => $"- {f} - {DescribeMemoryFile(Path.Combine(directory, f!))}");
        return "\n" + string.Join("\n", lines);
    }

    private static string DescribeMemoryFile(string filePath)
    {
        var filename = Path.GetFileName(filePath);

        try
        {
            var firstLine = File.ReadLines(filePath)
                .Select(l => l.TrimStart('#', ' ').Trim())
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

            if (string.IsNullOrWhiteSpace(firstLine))
                return filename;

            return firstLine.Length > 100 ? firstLine[..100] : firstLine;
        }
        catch (IOException)
        {
            return filename;
        }
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
