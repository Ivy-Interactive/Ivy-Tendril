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
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);
        PromptwareHelper.RequireProgramFolder(programFolder, settings.Name, tendrilHome);

        var memoryDir = Path.Combine(programFolder, "Memory");
        var resolvedPath = PromptwareMemoryResolver.Resolve(memoryDir, settings.Filename);

        if (resolvedPath is null)
        {
            var normalized = PromptwareMemoryResolver.NormalizeName(settings.Filename);

            var available = Directory.Exists(memoryDir)
                ? string.Join(", ", Directory.EnumerateFiles(memoryDir)
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f) && !f!.StartsWith('.'))
                    .OrderBy(f => f, StringComparer.Ordinal))
                : "";
            if (string.IsNullOrEmpty(available))
                available = "(none)";

            var suggestions = PromptwareMemoryResolver.Suggest(memoryDir, settings.Filename);
            var didYouMean = suggestions.Count > 0 ? $"\nDid you mean: {suggestions[0]}?" : "";

            var message = $"Memory file not found: {normalized} (promptware: {settings.Name})\n" +
                          $"Available memories: {available}{didYouMean}\n" +
                          $"This memory may have been pruned. Run `tendril promptware list-memory {settings.Name}` for the current list, and do not re-reference a memory that no longer exists.";

            throw new FileNotFoundException(message, Path.Combine(memoryDir, normalized));
        }

        Console.Write(File.ReadAllText(resolvedPath));
        return 0;
    }
}
