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
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);
        PromptwareHelper.RequireProgramFolder(programFolder, settings.Name, tendrilHome);

        var memoryDir = Path.Combine(programFolder, "Memory");

        if (settings.Filenames.Length == 1)
        {
            var rawFilename = settings.Filenames[0];
            var resolvedPath = PromptwareMemoryResolver.Resolve(memoryDir, rawFilename);

            if (resolvedPath is null)
            {
                throw BuildFileNotFoundException(memoryDir, settings.Name, rawFilename);
            }

            writer.Write(File.ReadAllText(resolvedPath));
            return 0;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < settings.Filenames.Length; i++)
        {
            var rawFilename = settings.Filenames[i];
            var resolvedPath = PromptwareMemoryResolver.Resolve(memoryDir, rawFilename);

            if (resolvedPath is null)
            {
                throw BuildFileNotFoundException(memoryDir, settings.Name, rawFilename);
            }

            var displayFilename = Path.GetFileName(resolvedPath);
            if (i > 0) sb.AppendLine();
            sb.AppendLine($"=== {displayFilename} ===");
            sb.AppendLine(File.ReadAllText(resolvedPath));
        }

        writer.Write(sb.ToString());
        return 0;
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

