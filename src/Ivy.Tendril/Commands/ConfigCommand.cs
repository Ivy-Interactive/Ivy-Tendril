using System.ComponentModel;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

// --- Settings ---

public class ConfigGetSettings : CommandSettings
{
    internal static readonly string[] ValidFields =
        ["codingAgent", "jobTimeout", "staleOutputTimeout", "gitTimeout", "maxConcurrentJobs", "planTemplate"];

    [Description("Config key (codingAgent, jobTimeout, staleOutputTimeout, gitTimeout, maxConcurrentJobs, planTemplate)")]
    [CommandArgument(0, "<key>")]
    public string Key { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.ValidateField(Key, ValidFields);
    }
}

public class ConfigSetSettings : CommandSettings
{
    [Description("Config key (codingAgent, jobTimeout, staleOutputTimeout, gitTimeout, maxConcurrentJobs, planTemplate)")]
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

    /// <summary>Number of value sources the user supplied (inline arg / --file / --stdin).</summary>
    public int SourceCount =>
        (Stdin ? 1 : 0) + (!string.IsNullOrEmpty(FilePath) ? 1 : 0) + (!string.IsNullOrEmpty(Value) ? 1 : 0);

    public override Spectre.Console.ValidationResult Validate()
    {
        if (SourceCount > 1)
            return Spectre.Console.ValidationResult.Error(
                "Provide the value in exactly one way: an inline <value>, --file, or --stdin.");

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
        _ => throw new ArgumentException(UnknownFieldMessage(field))
    };

    internal static string UnknownFieldMessage(string field) =>
        $"Unknown field: {field}. Valid fields: {string.Join(", ", ConfigGetSettings.ValidFields)}";
}

public class ConfigSetCommand(IAgentRunner runner) : Command<ConfigSetSettings>
{
    protected override int Execute(CommandContext context, ConfigSetSettings settings, CancellationToken cancellationToken)
    {
        var value = settings.Stdin ? Console.In.ReadToEnd()
            : !string.IsNullOrEmpty(settings.FilePath) ? File.ReadAllText(settings.FilePath)
            : settings.Value;

        var config = new ConfigService();
        // throws on bad int / out-of-range / unknown coding agent, before any write
        ApplyField(config.Settings, settings.Key, value, runner.RegisteredAgents);
        config.SaveSettings();

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
            case "maxconcurrentjobs": s.MaxConcurrentJobs = ParseBoundedInt(value, "maxConcurrentJobs", 1, 100); break;
            case "plantemplate": s.PlanTemplate = value; break;
            default: throw new ArgumentException(ConfigGetCommand.UnknownFieldMessage(field));
        }
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
