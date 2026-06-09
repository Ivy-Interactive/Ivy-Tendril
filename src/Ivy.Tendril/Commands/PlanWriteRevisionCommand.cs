using System.ComponentModel;
using Ivy.Tendril.Helpers;
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
}

public class PlanWriteRevisionCommand : Command<PlanWriteRevisionSettings>
{
    protected override int Execute(CommandContext context, PlanWriteRevisionSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var revisionsDir = Path.Combine(planFolder, "Revisions");
        Directory.CreateDirectory(revisionsDir);

        var number = ResolveRevisionNumber(revisionsDir);
        var filename = $"{number:D3}.md";
        var filePath = Path.Combine(revisionsDir, filename);

        var content = !string.IsNullOrEmpty(settings.FilePath)
            ? File.ReadAllText(settings.FilePath)
            : ConsoleHelper.ReadStdinWithTimeout();
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or pipe to STDIN)");

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }

    private static int ResolveRevisionNumber(string revisionsDir)
    {
        var max = 0;
        foreach (var file in Directory.GetFiles(revisionsDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name, out var num) && num > max)
                max = num;
        }
        return max + 1;
    }
}
