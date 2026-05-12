using System.Reflection;

namespace Ivy.Tendril.Services;

public static class FirmwareCompiler
{
    private static readonly Lazy<string?> PlansReference = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Ivy.Tendril.Assets.Plans.md");
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

        In the header above your arguments is specified.

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

        Complete your task and present the user with a summary.

        ## Reflection

        Every execution needs to end with a reflection step. This is your opportunity to improve over time. What did we learn during this session. Save this in an applicable markdown file under {PROGRAMFOLDER}/Memory/.

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
            .Select(kv => $"{kv.Key}: {kv.Value}"));

        var toolsListing = ListDirectoryFiles(Path.Combine(context.ProgramFolder, "Tools"));
        var memoryListing = ListDirectoryFiles(Path.Combine(context.ProgramFolder, "Memory"));

        var firmware = FirmwareTemplate
            .Replace("{HEADER}", header)
            .Replace("{PROGRAMFOLDER}", context.ProgramFolder)
            .Replace("{TOOLS}", toolsListing)
            .Replace("{MEMORY}", memoryListing);

        // Include Program.md inline
        var programFile = Path.Combine(context.ProgramFolder, "Program.md");
        if (File.Exists(programFile))
        {
            firmware += "\n\n## Program\n\n";
            firmware += File.ReadAllText(programFile) + "\n";
        }

        var plansContent = PlansReference.Value;
        if (plansContent != null)
        {
            firmware += "\n\n## Reference Documents\n";
            firmware += $"\n### Plans\n\n{plansContent}\n";
        }

        if (!string.IsNullOrWhiteSpace(context.CustomInstructions))
        {
            firmware += "\n\n## Custom Instructions\n\n";
            firmware += "IMPORTANT: The following instructions are provided by the user and take precedence over the Firmware template and Program.md instructions. Follow them even if they conflict with other instructions.\n\n";
            firmware += context.CustomInstructions + "\n";
        }

        return firmware;
    }

    private static string ListDirectoryFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return "(none)";

        var files = Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .OrderBy(f => f)
            .ToList();

        return files.Count == 0 ? "(none)" : string.Join(", ", files);
    }

    public static string GetNextLogFile(string programFolder)
    {
        var logsFolder = Path.Combine(programFolder, "Logs");
        Directory.CreateDirectory(logsFolder);

        var maxNumber = 0;
        foreach (var file in Directory.GetFiles(logsFolder, "*.md"))
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(baseName, out var num) && num > maxNumber)
                maxNumber = num;
        }

        // Use CreateNew to atomically claim the slot; retry on collision
        for (var attempt = maxNumber + 1; attempt < maxNumber + 100; attempt++)
        {
            var logFile = Path.Combine(logsFolder, $"{attempt:D5}.md");
            try
            {
                using var fs = new FileStream(logFile, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(fs);
                writer.Write("*Execution in progress...*\n");
                return logFile;
            }
            catch (IOException)
            {
                // File already exists (race with another process), try next number
            }
        }

        // Fallback: should never reach here
        var fallback = Path.Combine(logsFolder, $"{maxNumber + 1:D5}.md");
        File.WriteAllText(fallback, "*Execution in progress...*\n");
        return fallback;
    }

}

public record FirmwareContext(
    string ProgramFolder,
    Dictionary<string, string> Values,
    string? CustomInstructions = null);
