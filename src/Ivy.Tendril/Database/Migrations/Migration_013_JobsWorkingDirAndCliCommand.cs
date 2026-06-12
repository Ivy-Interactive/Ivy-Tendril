using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_013_JobsWorkingDirAndCliCommand : IMigration
{
    public int Version => 13;
    public string Description => "Add WorkingDirectory and CliCommand columns to Jobs table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Jobs ADD COLUMN WorkingDirectory TEXT;
            ALTER TABLE Jobs ADD COLUMN CliCommand TEXT;
            PRAGMA user_version = 13;
            """;
        cmd.ExecuteNonQuery();
    }
}
