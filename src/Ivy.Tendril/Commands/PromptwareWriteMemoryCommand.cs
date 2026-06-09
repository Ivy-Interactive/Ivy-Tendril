using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareWriteMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., UpdatePlan)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to write (e.g., pattern-name.md)")]
    [CommandArgument(1, "<filename>")]
    public string Filename { get; set; } = "";
}

public class PromptwareWriteMemoryCommand : Command<PromptwareWriteMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareWriteMemorySettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);

        var memoryDir = Path.Combine(programFolder, "Memory");
        Directory.CreateDirectory(memoryDir);

        var filename = Path.GetFileName(settings.Filename);
        var filePath = Path.Combine(memoryDir, filename);

        var content = ConsoleHelper.ReadStdinWithTimeout();
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (pipe to STDIN)");

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
