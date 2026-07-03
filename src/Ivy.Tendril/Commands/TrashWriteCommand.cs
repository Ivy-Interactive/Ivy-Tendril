using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class TrashWriteSettings : CommandSettings
{
    [Description("Filename to write (e.g., DuplicateTitle.md)")]
    [CommandArgument(0, "<filename>")]
    public string Filename { get; set; } = "";

    [Description("Read content from file instead of STDIN")]
    [CommandOption("--file|-f")]
    public string? FilePath { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(Filename, "filename");
    }
}

public class TrashWriteCommand : Command<TrashWriteSettings>
{
    protected override int Execute(CommandContext context, TrashWriteSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
            throw new InvalidOperationException("TENDRIL_HOME not set");

        var trashDir = Path.Combine(tendrilHome, "Trash");
        Directory.CreateDirectory(trashDir);

        var filename = Path.GetFileName(settings.Filename);
        var filePath = Path.Combine(trashDir, filename);

        var content = ConsoleHelper.ResolveContent(settings.FilePath, () => ConsoleHelper.ReadStdinWithTimeout());
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or pipe to STDIN)");

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
