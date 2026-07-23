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

    [Description("Read content from this file")]
    [CommandOption("--file|-f")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read content from standard input")]
    public bool Stdin { get; set; }

    public int SourceCount => CliValidation.CountSources(Stdin, FilePath, "");

    public override Spectre.Console.ValidationResult Validate()
    {
        var sourceValidation = CliValidation.ValidateSingleSource(SourceCount, "--file or --stdin");
        if (!sourceValidation.Successful)
            return sourceValidation;

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

        var content = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, null);
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or --stdin)");

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
