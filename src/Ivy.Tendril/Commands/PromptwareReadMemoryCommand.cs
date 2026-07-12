using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareReadMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., SetupProject)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename to read (e.g., cli-quirks.md)")]
    [CommandArgument(1, "<filename>")]
    public string Filename { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.RequireNonEmpty(Filename, "filename")
        );
    }
}

public class PromptwareReadMemoryCommand : Command<PromptwareReadMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareReadMemorySettings settings, CancellationToken cancellationToken)
    {
        var workspaceDir = Directory.GetCurrentDirectory();
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workspaceDir);

        if (vaultPath != null)
        {
            var noteName = Path.GetFileNameWithoutExtension(settings.Filename);
            try
            {
                var bwPath = PromptwareHelper.GetBwPath();
                var arguments = vaultPath != null ? $"--vault \"{vaultPath}\" read {noteName}" : $"read {noteName}";
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
                if (proc != null)
                {
                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode == 0)
                    {
                        Console.Write(stdout);
                        return 0;
                    }
                    else if (stderr.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FileNotFoundException($"Memory note '{noteName}' not found in vault.", noteName);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch { /* fallback to file-based read if CLI fails */ }
        }

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var memoryDir = PromptwareHelper.ResolveMemoryDirectory(settings.Name, tendrilHome);
        var filename = Path.GetFileName(settings.Filename);
        if (!filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".md";
        }
        var filePath = Path.Combine(memoryDir, filename);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Memory file not found: {filename}", filePath);

        Console.Write(File.ReadAllText(filePath));
        return 0;
    }
}
