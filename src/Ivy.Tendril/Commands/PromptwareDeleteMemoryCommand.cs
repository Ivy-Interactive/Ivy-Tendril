using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareDeleteMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., SetupProject)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to delete (e.g., cli-quirks.md)")]
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

public class PromptwareDeleteMemoryCommand : Command<PromptwareDeleteMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareDeleteMemorySettings settings, CancellationToken cancellationToken)
    {
        var filename = Path.GetFileName(settings.Filename);

        if (filename.StartsWith('.'))
        {
            Console.Error.WriteLine($"Refusing to delete dotfile: {filename}");
            return 1;
        }

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);

        var memoryDir = Path.Combine(programFolder, "Memory");
        var filePath = Path.Combine(memoryDir, filename);

        if (!File.Exists(filePath))
        {
            Console.Write($"Memory file not found (nothing to delete): {filename}");
            return 0;
        }

        File.Delete(filePath);
        Console.Write(filePath);
        return 0;
    }
}
