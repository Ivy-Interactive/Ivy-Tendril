using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_014_JobsCleared : IMigration
{
    public int Version => 14;
    public string Description => "Add Cleared column to Jobs table for soft-clearing jobs from the Jobs app";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Jobs ADD COLUMN Cleared INTEGER NOT NULL DEFAULT 0;
            PRAGMA user_version = 14;
            """;
        cmd.ExecuteNonQuery();
    }
}
