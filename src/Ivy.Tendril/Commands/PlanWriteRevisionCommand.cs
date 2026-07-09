using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanWriteRevisionSettings : CommandSettings
{
    [Description("Plan ID or folder path")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

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

        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

public class PlanWriteRevisionCommand : Command<PlanWriteRevisionSettings>
{
    protected override int Execute(CommandContext context, PlanWriteRevisionSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        var content = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, null);
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or --stdin)");

        var filePath = RevisionWriter.WriteNext(planFolder, content, new ConfigService());
        Console.Write(filePath);
        return 0;
    }
}
