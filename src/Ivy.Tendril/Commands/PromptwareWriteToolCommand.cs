using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareWriteToolSettings : CommandSettings
{
    [Description("Promptware name (e.g., CreatePlan)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to write (e.g., tool-name.md)")]
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

public class PromptwareWriteToolCommand : Command<PromptwareWriteToolSettings>
{
    protected override int Execute(CommandContext context, PromptwareWriteToolSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);

        var toolsDir = Path.Combine(programFolder, "Tools");
        Directory.CreateDirectory(toolsDir);

        var filename = Path.GetFileName(settings.Filename);
        var filePath = Path.Combine(toolsDir, filename);

        var content = ConsoleHelper.ReadStdinWithTimeout();
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (pipe to STDIN)");

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
