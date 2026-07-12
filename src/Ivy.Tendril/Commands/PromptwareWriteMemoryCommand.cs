using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareWriteMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., UpdatePlan)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to write (e.g., pattern-name.md)")]
    [CommandArgument(1, "<filename>")]
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

        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.RequireNonEmpty(Filename, "filename")
        );
    }
}

public class PromptwareWriteMemoryCommand : Command<PromptwareWriteMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareWriteMemorySettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var memoryDir = PromptwareHelper.ResolveMemoryDirectory(settings.Name, tendrilHome);
        Directory.CreateDirectory(memoryDir);

        var filename = Path.GetFileName(settings.Filename);
        if (!filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".md";
        }
        var filePath = Path.Combine(memoryDir, filename);

        var content = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, null);
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or --stdin)");

        var workspaceDir = Directory.GetCurrentDirectory();
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workspaceDir);

        var noteName = Path.GetFileNameWithoutExtension(filename);
        var noteFile = Path.Combine(memoryDir, noteName + ".md");

        if (!File.Exists(noteFile))
        {
            try
            {
                var bwPath = PromptwareHelper.GetBwPath();
                var arguments = vaultPath != null ? $"add {noteName}" : $"add {noteName} --global";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = bwPath,
                    Arguments = arguments,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit();
            }
            catch { /* fallback if CLI execution fails */ }
        }

        File.WriteAllText(filePath, content);
        Console.Write(filePath);
        return 0;
    }
}
