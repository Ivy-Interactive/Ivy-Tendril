using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareReadMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., UpdateProject)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to read (e.g., cli-quirks.md)")]
    [CommandArgument(1, "<filename>")]
    public string Filename { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.RequireNonEmpty(Filename, "filename")
        );
    }
}

public class PromptwareReadMemoryCommand : Command<PromptwareReadMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareReadMemorySettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);

        var memoryDir = Path.Combine(programFolder, "Memory");
        var filename = Path.GetFileName(settings.Filename);
        var filePath = Path.Combine(memoryDir, filename);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Memory file not found: {filename}", filePath);

        Console.Write(File.ReadAllText(filePath));
        return 0;
    }
}
