using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class VerificationListSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit a machine-readable JSON array of { name, prompt } with the full, untruncated prompt")]
    public bool Json { get; set; }
}

public class VerificationAddSettings : CommandSettings
{
    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("-p|--prompt")]
    [Description("Verification prompt (inline; use --file/--stdin for long text)")]
    public string? Prompt { get; set; }

    [CommandOption("--file|-f")]
    [Description("Read the prompt verbatim from this file (good for long/multiline prompts)")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read the prompt verbatim from standard input")]
    public bool Stdin { get; set; }

    public int SourceCount => CliValidation.CountSources(Stdin, FilePath, Prompt ?? "");

    public override Spectre.Console.ValidationResult Validate()
    {
        var sourceValidation = CliValidation.ValidateSingleSource(SourceCount, "--prompt, --file, or --stdin");
        if (!sourceValidation.Successful)
            return sourceValidation;

        return CliValidation.RequireNonEmpty(Name, "name");
    }
}

public class VerificationRemoveSettings : CommandSettings
{
    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(Name, "name");
    }
}

public class VerificationGetSettings : CommandSettings
{
    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(Name, "name");
    }
}

public class VerificationSetSettings : CommandSettings
{
    private static readonly string[] ValidFields = ["name", "prompt"];

    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Field name (name, prompt)")]
    [CommandArgument(1, "<field>")]
    public string Field { get; set; } = "";

    [Description("New value. Omit when using --file or --stdin. Use --file/--stdin for long or multiline prompts.")]
    [CommandArgument(2, "[value]")]
    public string Value { get; set; } = "";

    [CommandOption("-f|--file")]
    [Description("Read the value verbatim from this file (good for multiline prompt)")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read the value verbatim from standard input")]
    public bool Stdin { get; set; }

    public int SourceCount => CliValidation.CountSources(Stdin, FilePath, Value);

    public override Spectre.Console.ValidationResult Validate()
    {
        var sourceValidation = CliValidation.ValidateSingleSource(SourceCount, "an inline <value>, --file, or --stdin");
        if (!sourceValidation.Successful)
            return sourceValidation;

        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.ValidateField(Field, ValidFields)
        );
    }
}

public class VerificationListCommand : Command<VerificationListSettings>
{
    protected override int Execute(CommandContext context, VerificationListSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var verifications = config.Settings.Verifications;

        if (settings.Json)
        {
            // Plain stdout (not AnsiConsole) so the output is clean, parseable JSON for agents.
            var payload = verifications.Select(v => new { name = v.Name, prompt = v.Prompt });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload));
            return 0;
        }

        if (verifications.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No verification definitions found.[/]");
            return 0;
        }

        var rows = verifications.Select(v => (IReadOnlyList<string>)new[] { v.Name, FormatPromptForDisplay(v.Prompt) });
        CliOutput.WriteTable(["Name", "Prompt"], rows);
        return 0;
    }

    private static string FormatPromptForDisplay(string prompt)
    {
        if (CliOutput.IsPlain)
        {
            var firstLine = prompt.Split('\n')[0].Trim();
            return firstLine.Length > 120 ? firstLine[..120] + "..." : firstLine;
        }

        return prompt.Length > 60 ? prompt[..60] + "..." : prompt;
    }
}

public class VerificationGetCommand : Command<VerificationGetSettings>
{
    protected override int Execute(CommandContext context, VerificationGetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var match = config.Settings.Verifications
            .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            CliValidation.ThrowVerificationNotFound(settings.Name, config.Settings.Verifications.Select(v => v.Name));

        Console.Write(match.Prompt);
        return 0;
    }
}

public class VerificationAddCommand : Command<VerificationAddSettings>
{
    protected override int Execute(CommandContext context, VerificationAddSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        // Read stdin before taking the config lock: ResolveInput can block on a pipe for its timeout.
        var prompt = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Prompt);
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Provide --prompt, --file, or --stdin");

        config.MutateAndSave(s =>
        {
            if (s.Verifications.Any(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Verification already exists: {settings.Name}");

            s.Verifications.Add(new VerificationConfig
            {
                Name = settings.Name,
                Prompt = prompt
            });
        });

        Console.WriteLine($"Added verification definition: {settings.Name}");
        return 0;
    }
}

public class VerificationRemoveCommand : Command<VerificationRemoveSettings>
{
    protected override int Execute(CommandContext context, VerificationRemoveSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var match = s.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                CliValidation.ThrowVerificationNotFound(settings.Name, s.Verifications.Select(v => v.Name));

            s.Verifications.Remove(match);
        });

        Console.WriteLine($"Removed verification definition: {settings.Name}");
        return 0;
    }
}

public class VerificationSetCommand : Command<VerificationSetSettings>
{
    protected override int Execute(CommandContext context, VerificationSetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        // Read stdin before taking the config lock: ResolveInput can block on a pipe for its timeout.
        var value = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Value);

        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("value is required (use an inline <value>, --file, or --stdin)");

        config.MutateAndSave(s =>
        {
            var match = s.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                CliValidation.ThrowVerificationNotFound(settings.Name, s.Verifications.Select(v => v.Name));

            switch (settings.Field.ToLower())
            {
                case "name":
                    match.Name = value;
                    break;
                case "prompt":
                    match.Prompt = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown field: {settings.Field}. Valid fields: name, prompt");
            }
        });

        var summary = value.Length <= 60 && !value.Contains('\n') ? $"to '{value}'" : $"({value.Length} chars)";
        Console.WriteLine($"Updated verification {settings.Field} {summary}");
        return 0;
    }
}
