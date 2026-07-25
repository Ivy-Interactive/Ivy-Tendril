using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareListMemorySettings : CommandSettings
{
    [Description("Promptware name (e.g., SetupProject)")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(Name, "name");
    }
}

public class PromptwareListMemoryCommand : Command<PromptwareListMemorySettings>
{
    protected override int Execute(CommandContext context, PromptwareListMemorySettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Name, tendrilHome);
        PromptwareHelper.RequireProgramFolder(programFolder, settings.Name, tendrilHome);

        var memoryDir = Path.Combine(programFolder, "Memory");
        if (!Directory.Exists(memoryDir))
            return 0;

        foreach (var name in Directory.EnumerateFiles(memoryDir)
                     .Select(Path.GetFileName)
                     .Where(f => !string.IsNullOrEmpty(f) && !f!.StartsWith('.'))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            Console.WriteLine(name);
        }

        return 0;
    }
}
