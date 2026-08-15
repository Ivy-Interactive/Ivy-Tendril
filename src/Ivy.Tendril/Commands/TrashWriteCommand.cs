using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class TrashWriteSettings : CommandSettings
{
    [Description("Filename to write (e.g., DuplicateTitle.md)")]
    [CommandArgument(0, "<filename>")]
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

        var content = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, null);
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or --stdin)");

        FileHelper.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
