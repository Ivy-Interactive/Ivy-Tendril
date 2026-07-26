using System.ComponentModel;
using System.Text;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareReadMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., SetupProject)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Filename(s) to read (e.g., cli-quirks.md or file1.md file2.md)")]
    [CommandArgument(1, "<filenames>")]
    public string[] Filenames { get; set; } = [];

    public override Spectre.Console.ValidationResult Validate()
    {
        var nameValidation = CliValidation.RequireNonEmpty(Name, "name");
        if (!nameValidation.Successful) return nameValidation;

        if (Filenames.Length == 0)
            return Spectre.Console.ValidationResult.Error("<filenames> is required");

        return Spectre.Console.ValidationResult.Success();
    }
}

public class PromptwareReadMemoryCommand : Command<PromptwareReadMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareReadMemorySettings settings, CancellationToken cancellationToken)
    {
        return ExecuteInternal(settings, Console.Out);
    }

    internal static int ExecuteInternal(PromptwareReadMemorySettings settings, TextWriter? outputWriter = null)
    {
        var writer = outputWriter ?? Console.Out;
        var workspaceDir = Directory.GetCurrentDirectory();
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workspaceDir);

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);
        PromptwareHelper.RequireProgramFolder(programFolder, settings.Name, tendrilHome);
        var memoryDir = Path.Combine(programFolder, "Memory");

        var contents = new List<string>();

        foreach (var rawFilename in settings.Filenames)
        {
            var readContent = ReadSingleMemory(settings.Name, rawFilename, vaultPath, memoryDir);
            contents.Add(readContent);
        }

        if (settings.Filenames.Length == 1)
        {
            writer.Write(contents[0]);
        }
        else
        {
            var sb = new StringBuilder();
            for (var i = 0; i < settings.Filenames.Length; i++)
            {
                var displayFilename = Path.GetFileName(settings.Filenames[i]);
                if (i > 0) sb.AppendLine();
                sb.AppendLine($"=== {displayFilename} ===");
                sb.AppendLine(contents[i]);
            }
            writer.Write(sb.ToString());
        }

        return 0;
    }

    private static string ReadSingleMemory(string promptwareName, string rawFilename, string? vaultPath, string? memoryDir)
    {
        if (vaultPath != null)
        {
            var noteName = Path.GetFileNameWithoutExtension(rawFilename);
            try
            {
                var bwPath = PromptwareHelper.GetBwPath();
                var arguments = $"--vault \"{vaultPath}\" read {noteName}";
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
                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                    {
                        return stdout;
                    }
                }
            }
            catch
            {
                // fallback to file-based read
            }
        }

        if (memoryDir != null && Directory.Exists(memoryDir))
        {
            var resolvedPath = PromptwareMemoryResolver.Resolve(memoryDir, rawFilename);
            if (resolvedPath != null && File.Exists(resolvedPath))
            {
                return File.ReadAllText(resolvedPath);
            }
        }

        var filename = Path.GetFileName(rawFilename);
        if (!filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".md";
        }

        var localVault = PromptwareHelper.ResolveBrainwaresVaultDir();
        if (localVault != null)
        {
            var localFilePath = Path.Combine(localVault, "memories", filename);
            if (File.Exists(localFilePath))
            {
                return File.ReadAllText(localFilePath);
            }

            var userProfileHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
            var projName = PromptwareHelper.FindProjectNameForPath(Directory.GetCurrentDirectory(), userProfileHome);
            if (!string.IsNullOrEmpty(projName))
            {
                var projFilePath = Path.Combine(localVault, "memories", projName, filename);
                if (File.Exists(projFilePath))
                {
                    return File.ReadAllText(projFilePath);
                }
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalFilePath = Path.Combine(userProfile, ".config", "brainwares", "memories", filename);
        if (File.Exists(globalFilePath))
        {
            return File.ReadAllText(globalFilePath);
        }

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? Path.Combine(userProfile, ".tendril");
        var templateFilePath = Path.Combine(tendrilHome, "Promptwares", promptwareName, "Memory", filename);
        if (File.Exists(templateFilePath))
        {
            return File.ReadAllText(templateFilePath);
        }

        throw BuildFileNotFoundException(memoryDir ?? "", promptwareName, rawFilename);
    }

    private static FileNotFoundException BuildFileNotFoundException(string memoryDir, string promptwareName, string filename)
    {
        var normalized = PromptwareMemoryResolver.NormalizeName(filename);

        var available = Directory.Exists(memoryDir)
            ? string.Join(", ", Directory.EnumerateFiles(memoryDir)
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f) && !f!.StartsWith('.'))
                .OrderBy(f => f, StringComparer.Ordinal))
            : "";
        if (string.IsNullOrEmpty(available))
            available = "(none)";

        var suggestions = PromptwareMemoryResolver.Suggest(memoryDir, filename);
        var didYouMean = suggestions.Count > 0 ? $"\nDid you mean: {suggestions[0]}?" : "";

        var message = $"Memory file not found: {normalized} (promptware: {promptwareName})\n" +
                      $"Available memories: {available}{didYouMean}\n" +
                      $"This memory may have been pruned. Run `tendril promptware list-memory {promptwareName}` for the current list, and do not re-reference a memory that no longer exists.";

        return new FileNotFoundException(message, Path.Combine(memoryDir, normalized));
    }
}

