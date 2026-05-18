using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareReadMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., UpdateProject)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to read (e.g., cli-quirks.md)")]
    [CommandArgument(1, "<filename>")]
    public string Filename { get; set; } = "";
}

public class PromptwareReadMemoryCommand : Command<PromptwareReadMemorySettings>
{
    private readonly ILogger<PromptwareReadMemoryCommand> _logger;

    public PromptwareReadMemoryCommand(ILogger<PromptwareReadMemoryCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PromptwareReadMemorySettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
            var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);

            var memoryDir = Path.Combine(programFolder, "Memory");
            var filename = Path.GetFileName(settings.Filename);
            var filePath = Path.Combine(memoryDir, filename);

            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"Memory file not found: {filename}");
                return 1;
            }

            Console.Write(File.ReadAllText(filePath));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read memory file {Filename} for {Name}", settings.Filename, settings.Name);
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
