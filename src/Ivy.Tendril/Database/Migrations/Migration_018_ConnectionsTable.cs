using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_018_ConnectionsTable : IMigration
{
    public int Version => 18;
    public string Description => "Create Connections table to store external integrations";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Connections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Provider TEXT NOT NULL,
                ConnectionString TEXT NOT NULL,
                Permissions TEXT NOT NULL,
                Created TEXT NOT NULL,
                Updated TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_connections_name ON Connections(Name);
            """;
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 18;";
        setVersionCmd.ExecuteNonQuery();
    }
}
