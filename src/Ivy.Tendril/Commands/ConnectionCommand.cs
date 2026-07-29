using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

// --- Settings ---

public class ConnectionAddSettings : CommandSettings
{
    [Description("Unique name for this connection")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Integration provider (Slack, Discord, GitHub)")]
    [CommandArgument(1, "<provider>")]
    public string Provider { get; set; } = "";

    [CommandOption("-c|--config <value>")]
    [Description("Raw configuration JSON (e.g. {\"Token\":\"...\"})")]
    public string Config { get; set; } = "";

    [CommandOption("-p|--permissions <value>")]
    [Description("Comma-separated list of allowed actions (default: *)")]
    public string Permissions { get; set; } = "*";

    [CommandOption("-f|--file <path>")]
    [Description("File containing config JSON")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read config JSON from standard input")]
    public bool Stdin { get; set; }
}

public class ConnectionRemoveSettings : CommandSettings
{
    [Description("Name of the connection to remove")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";
}

public class ConnectionRunSettings : CommandSettings
{
    [Description("Name of the connection")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Action to run")]
    [CommandArgument(1, "<action>")]
    public string Action { get; set; } = "";

    [CommandOption("-a|--args <value>")]
    [Description("JSON arguments for the action")]
    public string Args { get; set; } = "";

    [CommandOption("-f|--file <path>")]
    [Description("File containing JSON arguments")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read JSON arguments from standard input")]
    public bool Stdin { get; set; }
}

public class ConnectionListSettings : CommandSettings
{
}

// --- Commands ---

public class ConnectionListCommand(IPlanDatabaseService db) : Command<ConnectionListSettings>
{
    private readonly IPlanDatabaseService _db = db;

    protected override int Execute(CommandContext context, ConnectionListSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        var connections = _db.GetConnections();
        if (connections.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No connections configured.[/]");
            return 0;
        }

        var table = new Spectre.Console.Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Name[/]");
        table.AddColumn("[bold]Provider[/]");
        table.AddColumn("[bold]Permissions[/]");
        table.AddColumn("[bold]Created[/]");
        table.AddColumn("[bold]Updated[/]");

        foreach (var conn in connections)
        {
            table.AddRow(
                conn.Name,
                conn.Provider,
                conn.Permissions,
                conn.Created.ToString("g"),
                conn.Updated.ToString("g")
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public class ConnectionAddCommand(IPlanDatabaseService db) : Command<ConnectionAddSettings>
{
    private readonly IPlanDatabaseService _db = db;

    protected override int Execute(CommandContext context, ConnectionAddSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            AnsiConsole.MarkupLine("[red]Error: Name cannot be empty.[/]");
            return 1;
        }

        var provider = settings.Provider.Trim();
        if (!provider.Equals("Slack", StringComparison.OrdinalIgnoreCase) &&
            !provider.Equals("Discord", StringComparison.OrdinalIgnoreCase) &&
            !provider.Equals("GitHub", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]Error: Provider must be Slack, Discord, or GitHub.[/]");
            return 1;
        }

        var configJson = Ivy.Tendril.Helpers.ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Config);
        if (string.IsNullOrWhiteSpace(configJson))
        {
            AnsiConsole.MarkupLine("[red]Error: Configuration cannot be empty.[/]");
            return 1;
        }

        if (!configJson.Trim().StartsWith('{'))
        {
            configJson = System.Text.Json.JsonSerializer.Serialize(new { Token = configJson.Trim() });
        }

        var now = DateTime.UtcNow;
        var existing = _db.GetConnectionByName(settings.Name);

        var connection = new ConnectionItem
        {
            Name = settings.Name,
            Provider = provider,
            ConnectionString = configJson,
            Permissions = string.IsNullOrWhiteSpace(settings.Permissions) ? "*" : settings.Permissions,
            Created = existing?.Created ?? now,
            Updated = now
        };

        _db.UpsertConnection(connection);
        AnsiConsole.MarkupLine($"[green]Successfully added/updated connection '{settings.Name}' ({provider}).[/]");
        return 0;
    }
}

public class ConnectionRemoveCommand(IPlanDatabaseService db) : Command<ConnectionRemoveSettings>
{
    private readonly IPlanDatabaseService _db = db;

    protected override int Execute(CommandContext context, ConnectionRemoveSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        var existing = _db.GetConnectionByName(settings.Name);
        if (existing == null)
        {
            AnsiConsole.MarkupLine($"[red]Error: Connection '{settings.Name}' not found.[/]");
            return 1;
        }

        _db.DeleteConnection(settings.Name);
        AnsiConsole.MarkupLine($"[green]Successfully removed connection '{settings.Name}'.[/]");
        return 0;
    }
}

public class ConnectionRunCommand(IPlanDatabaseService db, IConnectionExecutorService executor) : AsyncCommand<ConnectionRunSettings>
{
    private readonly IPlanDatabaseService _db = db;
    private readonly IConnectionExecutorService _executor = executor;

    protected override async Task<int> ExecuteAsync(CommandContext context, ConnectionRunSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        var connection = _db.GetConnectionByName(settings.Name);
        if (connection == null)
        {
            AnsiConsole.MarkupLine($"[red]Error: Connection '{settings.Name}' not found.[/]");
            return 1;
        }

        var isAllowed = false;
        var perms = connection.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in perms)
        {
            if (p == "*" || string.Equals(p, settings.Action, StringComparison.OrdinalIgnoreCase))
            {
                isAllowed = true;
                break;
            }
        }

        if (!isAllowed)
        {
            AnsiConsole.MarkupLine($"[red]Permission Denied: Connection '{settings.Name}' does not have permission to run action '{settings.Action}'.[/]");
            return 1;
        }

        var argsJson = Ivy.Tendril.Helpers.ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Args);
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            argsJson = "{}";
        }

        var (success, result) = await _executor.ExecuteActionAsync(connection, settings.Action, argsJson);
        if (success)
        {
            Console.Write(result);
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error: {result}[/]");
            return 1;
        }
    }
}
