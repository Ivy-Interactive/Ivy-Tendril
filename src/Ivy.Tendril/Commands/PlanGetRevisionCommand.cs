using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanGetRevisionSettings : CommandSettings
{
    [Description("Plan ID or folder path")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Print the latest revision (default behavior)")]
    [CommandOption("--latest")]
    public bool Latest { get; set; }

    [Description("Print a specific numbered revision (e.g. 2 for 002.md)")]
    [CommandOption("--number|-n")]
    public int? Number { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

public class PlanGetRevisionCommand : Command<PlanGetRevisionSettings>
{
    protected override int Execute(CommandContext context, PlanGetRevisionSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var revisionsDir = Path.Combine(planFolder, "Revisions");

        string filePath;
        if (settings.Number.HasValue)
        {
            filePath = Path.Combine(revisionsDir, $"{settings.Number.Value:D3}.md");
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Revision {settings.Number.Value:D3} not found for plan {settings.PlanId}", filePath);
        }
        else
        {
            filePath = FindLatestRevision(revisionsDir)
                ?? throw new FileNotFoundException($"No revisions found for plan {settings.PlanId} in {revisionsDir}");
        }

        Console.Write(FileHelper.ReadAllText(filePath));
        return 0;
    }

    private static string? FindLatestRevision(string revisionsDir)
    {
        if (!Directory.Exists(revisionsDir))
            return null;

        string? latestFile = null;
        var latestNumber = -1;
        foreach (var file in Directory.GetFiles(revisionsDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name, out var num) && num > latestNumber)
            {
                latestNumber = num;
                latestFile = file;
            }
        }

        return latestFile;
    }
}
