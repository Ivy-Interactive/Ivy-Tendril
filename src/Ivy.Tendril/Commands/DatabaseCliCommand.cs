using System.ComponentModel;
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
    protected override int Execute(CommandContext context, DbVersionSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbVersionInternal(dbPath);
    }
}

public class DbMigrateCommand : Command<DbMigrateSettings>
{
    protected override int Execute(CommandContext context, DbMigrateSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbMigrateInternal(dbPath);
    }
}

public class DbResetCommand : Command<DbResetSettings>
{
    protected override int Execute(CommandContext context, DbResetSettings settings, CancellationToken cancellationToken)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbResetInternal(dbPath, settings.Force);
    }
}
