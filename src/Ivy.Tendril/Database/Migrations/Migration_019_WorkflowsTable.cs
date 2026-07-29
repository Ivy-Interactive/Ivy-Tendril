using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_019_WorkflowsTable : IMigration
{
    public int Version => 19;
    public string Description => "Create Workflows table to store custom automations";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Workflows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                Definition TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Created TEXT NOT NULL,
                Updated TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_workflows_name ON Workflows(Name);
            """;
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 19;";
        setVersionCmd.ExecuteNonQuery();
    }
}
