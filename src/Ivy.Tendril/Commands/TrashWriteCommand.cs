using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class TrashWriteSettings : CommandSettings
{
    [Description("Filename to write (e.g., DuplicateTitle.md)")]
    [CommandArgument(0, "<filename>")]
    public string Filename { get; set; } = "";
}

public class TrashWriteCommand : Command<TrashWriteSettings>
{
    private readonly ILogger<TrashWriteCommand> _logger;

    public TrashWriteCommand(ILogger<TrashWriteCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, TrashWriteSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
            if (string.IsNullOrEmpty(tendrilHome))
                throw new InvalidOperationException("TENDRIL_HOME not set");

            var trashDir = Path.Combine(tendrilHome, "Trash");
            Directory.CreateDirectory(trashDir);

            var filename = Path.GetFileName(settings.Filename);
            var filePath = Path.Combine(trashDir, filename);

            var content = ConsoleHelper.ReadStdinWithTimeout();
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("No content provided (pipe to STDIN)");

            File.WriteAllText(filePath, content);
            Console.WriteLine(filePath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write trash file {Filename}", settings.Filename);
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
