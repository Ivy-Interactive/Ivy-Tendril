using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_011_JobsTypedArgs : IMigration
{
    public int Version => 11;
    public string Description => "Add TypedArgs column to Jobs table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        if (!ColumnExists(connection, "Jobs", "TypedArgs"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN TypedArgs TEXT;";
            cmd.ExecuteNonQuery();
        }

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 11;";
        setVersionCmd.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(reader.GetOrdinal("name"));
            if (name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
