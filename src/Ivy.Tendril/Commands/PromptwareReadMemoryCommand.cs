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
        var memoryDir = Path.Combine(programFolder, "Memory");

        if (settings.Filenames.Length == 1)
        {
            var filename = Path.GetFileName(settings.Filenames[0]);
            var filePath = Path.Combine(memoryDir, filename);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Memory file not found: {filename}", filePath);

            writer.Write(File.ReadAllText(filePath));
            return 0;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < settings.Filenames.Length; i++)
        {
            var filename = Path.GetFileName(settings.Filenames[i]);
            var filePath = Path.Combine(memoryDir, filename);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Memory file not found: {filename}", filePath);

            if (i > 0) sb.AppendLine();
            sb.AppendLine($"=== {filename} ===");
            sb.AppendLine(File.ReadAllText(filePath));
        }

        writer.Write(sb.ToString());
        return 0;
    }
}

