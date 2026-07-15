using Ivy.Tendril.Database;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class RunCommand : AsyncCommand<RunCommand.Settings>
{
    private readonly IServiceProvider _serviceProvider;

    public RunCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-p|--port")]
        public int? Port { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = _serviceProvider.GetRequiredService<IConfigService>();
        if (string.IsNullOrEmpty(config.TendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME is not configured. Run onboarding first.[/]");
            return 1;
        }
        var dbPath = Path.Combine(config.TendrilHome, "tendril.db");

        // Ensure directory exists
        var dbDir = Path.GetDirectoryName(dbPath);
        if (dbDir != null && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        AnsiConsole.MarkupLine("[blue]Checking database status...[/]");

        try
        {
            using var connection = SqliteConnectionFactory.OpenConfigured(dbPath);

            var migrator = new DatabaseMigrator(connection);
            var current = migrator.GetCurrentVersion();
            var latest = migrator.GetLatestVersion();

            if (current < latest)
            {
                AnsiConsole.MarkupLine($"[yellow]Migrating database from version {current} to {latest}...[/]");
                migrator.ApplyMigrations();
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Database migration failed: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }

        var server = TendrilServer.Create([], new Services.TendrilArgs());
        if (settings.Port.HasValue)
        {
            server.Args.Port = settings.Port.Value;
        }

        if (IsPortInUse(server.Args.Port))
        {
            AnsiConsole.MarkupLine($"[red]Error: Port {server.Args.Port} is already in use.[/]");
            AnsiConsole.MarkupLine("[yellow]Please make sure another instance of Tendril is not already running.[/]");
            AnsiConsole.MarkupLine("");
            if (OperatingSystem.IsWindows())
            {
                AnsiConsole.MarkupLine($"To find the process using this port, run: [blue]netstat -ano | findstr :{server.Args.Port}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"To find the process using this port, run: [blue]lsof -i :{server.Args.Port}[/]");
            }
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("To use a different port, use the [green]--port[/] option (e.g., [green]tendril run --port 5011[/]).");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Starting Ivy Tendril server on localhost:{server.Args.Port}...[/]");

        await server.RunAsync();

        return 0;
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
