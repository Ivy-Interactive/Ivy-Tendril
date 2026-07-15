using System.ComponentModel;
using System.IO;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class GenerateCertsCommand : Command<GenerateCertsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<OUTPUT_DIR>")]
        [Description("Directory where the localhost.pfx and localhost.crt certificates should be written")]
        public string OutputDir { get; set; } = string.Empty;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var outputDir = Path.GetFullPath(settings.OutputDir);
            Directory.CreateDirectory(outputDir);

            var pfxPath = Path.Combine(outputDir, "localhost.pfx");
            var crtPath = Path.Combine(outputDir, "localhost.crt");

            // Generate and save certificates using Ivy.Desktop's helper
            Ivy.Desktop.CertificateHelper.GenerateAndSaveCertificate(pfxPath, crtPath);

            AnsiConsole.MarkupLine($"[green]Successfully generated certificates at:[/] {outputDir}");
            return 0;
        }
        catch (System.Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error generating certificates:[/] {ex.Message}");
            return 1;
        }
    }
}
