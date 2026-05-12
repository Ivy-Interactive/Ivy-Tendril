using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_011_JobsTypedArgs : IMigration
{
    public int Version => 11;
    public string Description => "Add TypedArgs column to Jobs table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN TypedArgs TEXT;";
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 11;";
        setVersionCmd.ExecuteNonQuery();
    }
}
