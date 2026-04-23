using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddLogSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Action name (e.g., CreatePlan, ExecutePlan)")]
    [CommandArgument(1, "<action>")]
    public string Action { get; set; } = "";

    [CommandOption("--summary")]
    [Description("Optional summary text")]
    public string? Summary { get; init; }
}

public class PlanAddLogCommand : Command<PlanAddLogSettings>
{
    private readonly ILogger<PlanAddLogCommand> _logger;

    public PlanAddLogCommand(ILogger<PlanAddLogCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanAddLogSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var logPath = WriteLog(planFolder, settings.Action, settings.Summary);
            Console.WriteLine(logPath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add log to plan {PlanId}", settings.PlanId);
            return 1;
        }
    }

    internal static string WriteLog(string planFolder, string action, string? summary = null)
    {
        var logsDir = Path.Combine(planFolder, "logs");
        Directory.CreateDirectory(logsDir);

        var maxNumber = 0;
        foreach (var file in Directory.GetFiles(logsDir, "*.md"))
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            var dashIdx = baseName.IndexOf('-');
            if (dashIdx > 0 && int.TryParse(baseName[..dashIdx], out var num) && num > maxNumber)
                maxNumber = num;
        }

        var logPath = Path.Combine(logsDir, $"{maxNumber + 1:D3}-{action}.md");

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var content = $"# {action}\n\n- **Completed:** {now}\n- **Status:** Completed";
        if (!string.IsNullOrEmpty(summary))
            content += $"\n\n{summary}";

        File.WriteAllText(logPath, content);
        return logPath;
    }
}
