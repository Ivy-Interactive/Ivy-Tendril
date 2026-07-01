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

    [Description("Read content from file instead of STDIN")]
    [CommandOption("--file|-f")]
    public string? FilePath { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

public class PlanWriteRevisionCommand : Command<PlanWriteRevisionSettings>
{
    protected override int Execute(CommandContext context, PlanWriteRevisionSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        var content = !string.IsNullOrEmpty(settings.FilePath)
            ? File.ReadAllText(settings.FilePath)
            : ConsoleHelper.ReadStdinWithTimeout();
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or pipe to STDIN)");

        var filePath = RevisionWriter.WriteNext(planFolder, content, new ConfigService());
        Console.Write(filePath);
        return 0;
    }
}
