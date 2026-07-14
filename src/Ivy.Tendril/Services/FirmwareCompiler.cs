using System.Reflection;
using System.Text;
using Ivy.Tendril.Helpers;

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
        // Check if we are running in a unit/integration test environment
        var isTest = Environment.CommandLine.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
                     Environment.CommandLine.Contains("vstest", StringComparison.OrdinalIgnoreCase) ||
                     Environment.CommandLine.Contains("xunit", StringComparison.OrdinalIgnoreCase);

        string? vaultPath = null;
        if (!isTest)
        {
            var workspaceDir = context.Values.TryGetValue("TendrilProject", out var proj) ? proj : null;
            if (string.IsNullOrEmpty(workspaceDir))
            {
                var projectDir = context.Values.TryGetValue("TendrilPlansFolder", out var plans) ? plans : null;
                workspaceDir = string.IsNullOrEmpty(projectDir) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(projectDir);
            }
            vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workspaceDir);
        }

        if (vaultPath != null)
        {
            var programName = Path.GetFileName(context.ProgramFolder).ToLowerInvariant();
            var basePrompt = "";
            
            try
            {
                var bwPath = PromptwareHelper.GetBwPath();
                var arguments = vaultPath != null ? $"--vault \"{vaultPath}\" compile {programName}" : $"compile {programName}";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = bwPath,
                    Arguments = arguments,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var stdout = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode == 0)
                    {
                        basePrompt = stdout;
                    }
                }
            }
            catch (Exception ex)
            {
                basePrompt = $"Error compiling program via brainwares CLI: {ex.Message}";
            }

            if (!string.IsNullOrEmpty(basePrompt))
            {
                var firmware = basePrompt;
                var headerValues = new Dictionary<string, string>(context.Values);

                if (!headerValues.ContainsKey("CurrentTime"))
                    headerValues["CurrentTime"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                var header = string.Join("\n", headerValues
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}: {NormalizeHeaderValue(kv.Key, kv.Value)}"));

                firmware = $"## System Headers\n{header}\n\n" + firmware;

                if (headerValues.TryGetValue("TendrilJobId", out var tendrilJobId) && !string.IsNullOrEmpty(tendrilJobId))
                {
                    firmware += $"\n\n**Status Reporting:** Use `tendril job status {tendrilJobId} --message \"your status\"` to report progress. " +
                                "You can also pass `--plan-id` and `--plan-title` to associate the job with a plan.\n";
                    firmware += $"\n**Failure Reporting:** On any unrecoverable failure, call `tendril job fail {tendrilJobId} --message \"<what failed and why>\"` before you `exit 1`, " +
                                "so the failure reason is reported cleanly instead of being guessed from your output.\n";
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
                    firmware += "IMPORTANT: The following instructions are provided by the user and take precedence over other instructions. Follow them even if they conflict.\n\n";
                    firmware += context.CustomInstructions + "\n";
                }

                var rules = GetBrainwaresRules();
                if (!string.IsNullOrEmpty(rules))
                {
                    firmware += "\n\n" + rules;
                }

                return firmware;
            }
        }

        // C# Fallback logic
        {
            var headerValues = new Dictionary<string, string>(context.Values);

            if (!headerValues.ContainsKey("CurrentTime"))
                headerValues["CurrentTime"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var header = string.Join("\n", headerValues
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}: {NormalizeHeaderValue(kv.Key, kv.Value)}"));

            var toolsListing = ListDirectoryFiles(Path.Combine(context.ProgramFolder, "Tools"), "(no tools yet)");
            
            var promptwareName = Path.GetFileName(context.ProgramFolder);
            var tendrilHome = headerValues.TryGetValue("TendrilHome", out var home) ? home : null;
            var memoryDir = PromptwareHelper.ResolveMemoryDirectory(promptwareName, tendrilHome);
            var memoryListing = ListDirectoryFiles(memoryDir, "(no memory yet)");

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

            var rules = GetBrainwaresRules();
            if (!string.IsNullOrEmpty(rules))
            {
                firmware += "\n\n" + rules;
            }

            return firmware;
        }
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

    private static string GetBrainwaresRules()
    {
        try
        {
            var bwPath = PromptwareHelper.GetBwPath();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = bwPath,
                Arguments = "rules",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    return stdout;
                }
            }
        }
        catch { }
        return "";
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
