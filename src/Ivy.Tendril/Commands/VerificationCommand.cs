using System.ComponentModel;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
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
}

public class VerificationRemoveSettings : CommandSettings
{
    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";
}

public class VerificationSetSettings : CommandSettings
{
    [Description("Verification name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Field name (name, prompt)")]
    [CommandArgument(1, "<field>")]
    public string Field { get; set; } = "";

    [Description("Field value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; set; } = "";
}

public class VerificationListCommand : Command<VerificationListSettings>
{
    private readonly ILogger<VerificationListCommand> _logger;

    public VerificationListCommand(ILogger<VerificationListCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, VerificationListSettings settings, CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list verification definitions");
            return 1;
        }
    }
}

public class VerificationAddCommand : Command<VerificationAddSettings>
{
    private readonly ILogger<VerificationAddCommand> _logger;

    public VerificationAddCommand(ILogger<VerificationAddCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, VerificationAddSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();

            if (config.Settings.Verifications.Any(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("Verification already exists: {Name}", settings.Name);
                return 1;
            }

            var prompt = settings.Prompt;
            if (string.IsNullOrEmpty(prompt))
            {
                if (!Console.IsInputRedirected)
                {
                    _logger.LogError("Provide --prompt or pipe content via stdin");
                    return 1;
                }
                prompt = Console.In.ReadToEnd().Trim();
            }

            config.Settings.Verifications.Add(new VerificationConfig
            {
                Name = settings.Name,
                Prompt = prompt
            });

            config.SaveSettings();
            _logger.LogInformation("Added verification definition: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add verification definition");
            return 1;
        }
    }
}

public class VerificationRemoveCommand : Command<VerificationRemoveSettings>
{
    private readonly ILogger<VerificationRemoveCommand> _logger;

    public VerificationRemoveCommand(ILogger<VerificationRemoveCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, VerificationRemoveSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var match = config.Settings.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Verification not found: {Name}", settings.Name);
                return 1;
            }

            config.Settings.Verifications.Remove(match);
            config.SaveSettings();
            _logger.LogInformation("Removed verification definition: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove verification definition");
            return 1;
        }
    }
}

public class VerificationSetCommand : Command<VerificationSetSettings>
{
    private readonly ILogger<VerificationSetCommand> _logger;

    public VerificationSetCommand(ILogger<VerificationSetCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, VerificationSetSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var match = config.Settings.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Verification not found: {Name}", settings.Name);
                return 1;
            }

            switch (settings.Field.ToLower())
            {
                case "name":
                    match.Name = settings.Value;
                    break;
                case "prompt":
                    match.Prompt = settings.Value;
                    break;
                default:
                    _logger.LogError("Unknown field: {Field}. Valid fields: name, prompt", settings.Field);
                    return 1;
            }

            config.SaveSettings();
            _logger.LogInformation("Updated verification {Field} to '{Value}'", settings.Field, settings.Value);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update verification definition");
            return 1;
        }
    }
}
