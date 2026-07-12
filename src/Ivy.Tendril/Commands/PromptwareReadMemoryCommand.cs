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

        var filename = Path.GetFileName(settings.Filename);
        if (!filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".md";
        }

        // 1. Try local memories directory
        var localVault = PromptwareHelper.ResolveBrainwaresVaultDir();
        if (localVault != null)
        {
            var localFilePath = Path.Combine(localVault, "memories", filename);
            if (File.Exists(localFilePath))
            {
                Console.Write(File.ReadAllText(localFilePath));
                return 0;
            }
            
            // Try subdirectory check (e.g. memories/<project_name>/filename)
            var projName = PromptwareHelper.FindProjectNameForPath(Directory.GetCurrentDirectory(), Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril"));
            if (!string.IsNullOrEmpty(projName))
            {
                var projFilePath = Path.Combine(localVault, "memories", projName, filename);
                if (File.Exists(projFilePath))
                {
                    Console.Write(File.ReadAllText(projFilePath));
                    return 0;
                }
            }
        }

        // 2. Try global memories directory
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalFilePath = Path.Combine(userProfile, ".config", "brainwares", "memories", filename);
        if (File.Exists(globalFilePath))
        {
            Console.Write(File.ReadAllText(globalFilePath));
            return 0;
        }

        // 3. Fallback to templates memory folder
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? Path.Combine(userProfile, ".tendril");
        var templateFilePath = Path.Combine(tendrilHome, "Promptwares", settings.Name, "Memory", filename);
        if (File.Exists(templateFilePath))
        {
            Console.Write(File.ReadAllText(templateFilePath));
            return 0;
        }

        throw new FileNotFoundException($"Memory file not found: {filename}");
    }
}
