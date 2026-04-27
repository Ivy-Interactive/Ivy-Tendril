using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Ivy.Tendril.Database;

namespace Ivy.Tendril.Commands;

public class DbVersionSettings : CommandSettings
{
}

public class DbMigrateSettings : CommandSettings
{
}

public class DbResetSettings : CommandSettings
{
    [CommandOption("--force")]
    [Description("Skip confirmation prompt")]
    public bool Force { get; init; }
}

public class DbVersionCommand : Command<DbVersionSettings>
{
    private readonly ILogger<DbVersionCommand> _logger;

    public DbVersionCommand(ILogger<DbVersionCommand> logger)
    {
        _logger = logger;
    }

    protected override int Execute(CommandContext context, DbVersionSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbVersionInternal(dbPath, _logger);
    }
}

public class DbMigrateCommand : Command<DbMigrateSettings>
{
    private readonly ILogger<DbMigrateCommand> _logger;

    public DbMigrateCommand(ILogger<DbMigrateCommand> logger)
    {
        _logger = logger;
    }

    protected override int Execute(CommandContext context, DbMigrateSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbMigrateInternal(dbPath, _logger);
    }
}

public class DbResetCommand : Command<DbResetSettings>
{
    private readonly ILogger<DbResetCommand> _logger;

    public DbResetCommand(ILogger<DbResetCommand> logger)
    {
        _logger = logger;
    }

    protected override int Execute(CommandContext context, DbResetSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbResetInternal(dbPath, settings.Force, _logger);
    }
}
