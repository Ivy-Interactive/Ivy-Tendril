using System.ComponentModel;
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
    public override int Execute(CommandContext context, DbVersionSettings settings)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            Console.Error.WriteLine("Error: TENDRIL_HOME environment variable is not set.");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbVersionInternal(dbPath);
    }
}

public class DbMigrateCommand : Command<DbMigrateSettings>
{
    public override int Execute(CommandContext context, DbMigrateSettings settings)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            Console.Error.WriteLine("Error: TENDRIL_HOME environment variable is not set.");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbMigrateInternal(dbPath);
    }
}

public class DbResetCommand : Command<DbResetSettings>
{
    public override int Execute(CommandContext context, DbResetSettings settings)
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            Console.Error.WriteLine("Error: TENDRIL_HOME environment variable is not set.");
            return 1;
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        return DatabaseCommands.DbResetInternal(dbPath, settings.Force);
    }
}
