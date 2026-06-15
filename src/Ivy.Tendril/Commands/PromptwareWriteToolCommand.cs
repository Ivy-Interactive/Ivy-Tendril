using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
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
}

public class PromptwareWriteToolCommand : Command<PromptwareWriteToolSettings>
{
    private readonly ILogger<PromptwareWriteToolCommand> _logger;

    public PromptwareWriteToolCommand(ILogger<PromptwareWriteToolCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PromptwareWriteToolSettings settings, CancellationToken cancellationToken)
    {
        try
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
            Console.WriteLine(filePath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write tool file {Filename} for {Name}", settings.Filename, settings.Name);
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
