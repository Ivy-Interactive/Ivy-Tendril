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

    [Description("Read content from file instead of STDIN")]
    [CommandOption("--file|-f")]
    public string? FilePath { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.RequireNonEmpty(Filename, "filename")
        );
    }
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

        var content = ConsoleHelper.ResolveContent(settings.FilePath, () => ConsoleHelper.ReadStdinWithTimeout());
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or pipe to STDIN)");

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
