using System.Reflection;

namespace Ivy.Tendril.Services.Agents;

public class FirmwareCompiler
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

        ## Logs

        In {PROGRAMFOLDER}/Logs/ we maintain logs for all executions of this application.

        A file for this session has already been created: {LOGFILE}

        It currently only have the args written to it.

        In the log file you are to maintain a record:

        - The outcome of the execution
        - Any tools created or changed
        - Any memory created or changed
        - And changes to the program during reflection

        ## Goal

        You are to execute the instructions in {PROGRAMFOLDER}/Program.md

        Your goal is to complete these instructions with the following priority:

        1. Completeness
        2. Speed
        3. Token efficiency
        4. Improvement over time

        To complete your task you have powershell tools stored in {PROGRAMFOLDER}/Tools/. You are urged to create and maintain reusable tools to better achieve your goals during this session and over time.

        You can store memory in {PROGRAMFOLDER}/Memory/ as markdown files.

        Always start with:

        - Read {PROGRAMFOLDER}/Program.md
        - List tools in {PROGRAMFOLDER}/Tools/
        - List memory in {PROGRAMFOLDER}/Memory/

        Complete you task and present the user with a summary.

        ## Reflection

        Every execution needs to end with a reflection step. This is your oppurtunity to improve over time. What did we learn during this session. Save this in a applicable markdown file under {PROGRAMFOLDER}/Memory/. Create new tools if applicable. Add instuctions to {PROGRAMFOLDER}/Program.md.

        - Note that learnings might be falsified over time. Pruning memory is just as important as storing new memory.
        - Many session don't have any new learnings. Only store memory when you need it.
        - Only store general knowledge in Program.md - this is shared between multiple users of this system. In Memory you should store specifics about this user's system.
        """;

    public static string Compile(FirmwareContext context)
    {
        var headerValues = new Dictionary<string, string>(context.Values);

        if (!headerValues.ContainsKey("CurrentTime"))
            headerValues["CurrentTime"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var header = string.Join("\n", headerValues
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}: {kv.Value}"));

        var firmware = FirmwareTemplate
            .Replace("{HEADER}", header)
            .Replace("{LOGFILE}", context.LogFile)
            .Replace("{PROGRAMFOLDER}", context.ProgramFolder);

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

    public static string GetNextLogFile(string programFolder, Dictionary<string, string>? initialValues = null)
    {
        var logsFolder = Path.Combine(programFolder, "Logs");
        Directory.CreateDirectory(logsFolder);

        var maxNumber = 0;
        if (Directory.Exists(logsFolder))
        {
            foreach (var file in Directory.GetFiles(logsFolder, "*.md"))
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(baseName, out var num) && num > maxNumber)
                    maxNumber = num;
            }
        }

        var logFile = Path.Combine(logsFolder, $"{maxNumber + 1:D5}.md");

        // Reserve the slot immediately to prevent race conditions with concurrent jobs
        var header = $"# Execution Log {maxNumber + 1:D5}\n\n## Args\n";
        if (initialValues != null)
        {
            foreach (var kv in initialValues.OrderBy(kv => kv.Key))
            {
                var value = kv.Value.Length > 200 ? kv.Value[..200] + "..." : kv.Value;
                header += $"- **{kv.Key}:** {value}\n";
            }
        }
        header += "\n*Execution in progress...*\n";
        File.WriteAllText(logFile, header);

        return logFile;
    }

    public static string ResolveProgramFolder(string promptsRoot, string promptwareName)
    {
        return Path.Combine(promptsRoot, promptwareName);
    }
}

public record FirmwareContext(
    string ProgramFolder,
    string LogFile,
    Dictionary<string, string> Values,
    string? CustomInstructions = null);
