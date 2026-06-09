using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class VerificationListSettings : CommandSettings { }

public class VerificationAddSettings : CommandSettings
{
    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("-p|--prompt")]
    [Description("Verification prompt (reads from stdin if omitted)")]
    public string? Prompt { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
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

    [Description("Field value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.ValidateField(Field, ValidFields),
            CliValidation.RequireNonEmpty(Value, "value")
        );
    }
}

public class VerificationListCommand : Command<VerificationListSettings>
{
    protected override int Execute(CommandContext context, VerificationListSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var verifications = config.Settings.Verifications;

        if (verifications.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No verification definitions found.[/]");
            return 0;
        }

        var table = new Spectre.Console.Table();
        table.AddColumn("Name");
        table.AddColumn("Prompt");

        foreach (var v in verifications)
        {
            var prompt = v.Prompt.Length > 60 ? v.Prompt[..60] + "..." : v.Prompt;
            table.AddRow(v.Name.EscapeMarkup(), prompt.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
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
            throw new InvalidOperationException($"Verification not found: {settings.Name}");

        Console.Write(match.Prompt);
        return 0;
    }
}

public class VerificationAddCommand : Command<VerificationAddSettings>
{
    protected override int Execute(CommandContext context, VerificationAddSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        if (config.Settings.Verifications.Any(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Verification already exists: {settings.Name}");

        var prompt = settings.Prompt;
        if (string.IsNullOrEmpty(prompt))
        {
            if (!Console.IsInputRedirected)
                throw new ArgumentException("Provide --prompt or pipe content via stdin");
            prompt = Console.In.ReadToEnd().Trim();
        }

        config.Settings.Verifications.Add(new VerificationConfig
        {
            Name = settings.Name,
            Prompt = prompt
        });

        config.SaveSettings();
        Console.WriteLine($"Added verification definition: {settings.Name}");
        return 0;
    }
}

public class VerificationRemoveCommand : Command<VerificationRemoveSettings>
{
    protected override int Execute(CommandContext context, VerificationRemoveSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var match = config.Settings.Verifications
            .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Verification not found: {settings.Name}");

        config.Settings.Verifications.Remove(match);
        config.SaveSettings();
        Console.WriteLine($"Removed verification definition: {settings.Name}");
        return 0;
    }
}

public class VerificationSetCommand : Command<VerificationSetSettings>
{
    protected override int Execute(CommandContext context, VerificationSetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var match = config.Settings.Verifications
            .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Verification not found: {settings.Name}");

        switch (settings.Field.ToLower())
        {
            case "name":
                match.Name = settings.Value;
                break;
            case "prompt":
                match.Prompt = settings.Value;
                break;
            default:
                throw new ArgumentException($"Unknown field: {settings.Field}. Valid fields: name, prompt");
        }

        config.SaveSettings();
        Console.WriteLine($"Updated verification {settings.Field} to '{settings.Value}'");
        return 0;
    }
}
