using System.ComponentModel;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Themes;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

// --- Settings ---

public class ConfigGetSettings : CommandSettings
{
    internal static readonly string[] ValidFields =
        ["codingAgent", "jobTimeout", "staleOutputTimeout", "gitTimeout", "maxConcurrentJobs", "planTemplate", "theme", "themeMode"];

    [Description("Config key (codingAgent, jobTimeout, staleOutputTimeout, gitTimeout, maxConcurrentJobs, planTemplate, theme, themeMode)")]
    [CommandArgument(0, "<key>")]
    public string Key { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.ValidateField(Key, ValidFields);
    }
}

public class ConfigSetSettings : CommandSettings
{
    [Description("Config key (codingAgent, jobTimeout, staleOutputTimeout, gitTimeout, maxConcurrentJobs, planTemplate, theme, themeMode)")]
    [CommandArgument(0, "<key>")]
    public string Key { get; set; } = "";

    [Description("New value. Omit when using --file or --stdin. Use --file/--stdin for long or multiline values.")]
    [CommandArgument(1, "[value]")]
    public string Value { get; set; } = "";

    [CommandOption("-f|--file")]
    [Description("Read the value verbatim from this file (good for multiline planTemplate)")]
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

        return CliValidation.ValidateField(Key, ConfigGetSettings.ValidFields);
    }
}

// --- Commands ---

public class ConfigGetCommand : Command<ConfigGetSettings>
{
    protected override int Execute(CommandContext context, ConfigGetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        // Write raw (like VerificationGetCommand) so values containing '[' or newlines
        // round-trip cleanly, e.g. `tendril config get planTemplate > tpl.txt`.
        Console.Write(ReadField(config.Settings, settings.Key));
        return 0;
    }

    internal static string ReadField(TendrilSettings s, string field) => field.ToLowerInvariant() switch
    {
        "codingagent" => s.CodingAgent,
        "jobtimeout" => s.JobTimeout.ToString(),
        "staleoutputtimeout" => s.StaleOutputTimeout.ToString(),
        "gittimeout" => s.GitTimeout.ToString(),
        "maxconcurrentjobs" => s.MaxConcurrentJobs.ToString(),
        "plantemplate" => s.PlanTemplate,
        "theme" => s.Theme,
        "thememode" => s.ThemeMode,
        _ => throw new ArgumentException(UnknownFieldMessage(field))
    };

    internal static string UnknownFieldMessage(string field) =>
        $"Unknown field: {field}. Valid fields: {string.Join(", ", ConfigGetSettings.ValidFields)}";
}

public class ConfigSetCommand(IAgentRunner runner) : Command<ConfigSetSettings>
{
    protected override int Execute(CommandContext context, ConfigSetSettings settings, CancellationToken cancellationToken)
    {
        // Read stdin before taking the config lock: ResolveInput can block on a pipe for its timeout.
        var value = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Value);

        var config = new ConfigService();
        // throws on bad int / out-of-range / unknown coding agent, before any write
        config.MutateAndSave(s => ApplyField(s, settings.Key, value, runner.RegisteredAgents));

        // Report the value actually stored (e.g. the canonical coding-agent id), not the raw input.
        var stored = ConfigGetCommand.ReadField(config.Settings, settings.Key);
        var summary = stored.Length <= 60 && !stored.Contains('\n') ? $"to '{stored}'" : $"({stored.Length} chars)";
        Console.WriteLine($"Updated {settings.Key} {summary}");
        return 0;
    }

    // Bounds mirror ConfigService.ValidateSettings() so a value written here survives a reload
    // instead of being silently reset to a default. Pass validCodingAgents (the runner's
    // RegisteredAgents) to validate the codingAgent value; omit it to skip that check.
    internal static void ApplyField(TendrilSettings s, string field, string value,
        IReadOnlyCollection<string>? validCodingAgents = null)
    {
        switch (field.ToLowerInvariant())
        {
            case "codingagent": s.CodingAgent = ValidateCodingAgent(value, validCodingAgents); break;
            case "jobtimeout": s.JobTimeout = ParseBoundedInt(value, "jobTimeout", 1, 480); break;
            case "staleoutputtimeout": s.StaleOutputTimeout = ParseBoundedInt(value, "staleOutputTimeout", 1, 60); break;
            case "gittimeout": s.GitTimeout = ParseBoundedInt(value, "gitTimeout", 1, 30); break;
            case "maxconcurrentjobs": s.MaxConcurrentJobs = ParseBoundedInt(value, "maxConcurrentJobs", 1, 512); break;
            case "plantemplate": s.PlanTemplate = value; break;
            case "theme": s.Theme = ValidateTheme(value); break;
            case "thememode": s.ThemeMode = ValidateThemeMode(value); break;
            default: throw new ArgumentException(ConfigGetCommand.UnknownFieldMessage(field));
        }
    }

    internal static string ValidateThemeMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("themeMode cannot be empty.");

        var trimmed = value.Trim();
        if (trimmed.Equals("light", StringComparison.OrdinalIgnoreCase))
            return "light";
        if (trimmed.Equals("dark", StringComparison.OrdinalIgnoreCase))
            return "dark";
        if (trimmed.Equals("system", StringComparison.OrdinalIgnoreCase))
            return "system";

        throw new ArgumentException(
            $"Unknown themeMode '{value}'. Valid modes: light, dark, system");
    }

    internal static string ValidateTheme(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("theme cannot be empty.");

        var match = TendrilThemes.All.FirstOrDefault(t => t.Id.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new ArgumentException(
                $"Unknown theme '{value}'. Valid themes: {string.Join(", ", TendrilThemes.All.Select(t => t.Id))}");

        return match.Id;
    }

    // Rejects empty and, when the known-agent set is supplied, unknown agents. Returns the
    // canonical registered id (preserving the runner's casing) so it resolves via GetCli later.
    internal static string ValidateCodingAgent(string value, IReadOnlyCollection<string>? validCodingAgents)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("codingAgent cannot be empty.");

        if (validCodingAgents is not { Count: > 0 })
            return value;

        var match = validCodingAgents.FirstOrDefault(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new ArgumentException(
                $"Unknown coding agent '{value}'. Valid agents: {string.Join(", ", validCodingAgents.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))}");

        return match;
    }

    internal static int ParseBoundedInt(string value, string field, int min, int max)
    {
        if (!int.TryParse(value.Trim(), out var n))
            throw new ArgumentException($"{field} must be an integer, got '{value}'.");
        if (n < min || n > max)
            throw new ArgumentException($"{field} must be between {min} and {max}, got {n}.");
        return n;
    }
}
