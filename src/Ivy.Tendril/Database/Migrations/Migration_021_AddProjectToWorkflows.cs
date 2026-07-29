using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_021_AddProjectToWorkflows : IMigration
{
    public int Version => 21;
    public string Description => "Add Project column to Workflows table and make Name unique per Project";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            -- 1. Rename existing table
            ALTER TABLE Workflows RENAME TO Workflows_old;

            -- 2. Create new table with Project column and UNIQUE(Name, Project) constraint
            CREATE TABLE Workflows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                Project TEXT NOT NULL DEFAULT 'default',
                Definition TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Created TEXT NOT NULL,
                Updated TEXT NOT NULL,
                UNIQUE(Name, Project)
            );

            -- 3. Copy data from old table to new table
            INSERT INTO Workflows (Id, Name, Description, Project, Definition, IsActive, Created, Updated)
            SELECT Id, Name, Description, 'default', Definition, IsActive, Created, Updated FROM Workflows_old;

            -- 4. Drop the old table
            DROP TABLE Workflows_old;

            -- 5. Create new index on (Name, Project)
            CREATE INDEX idx_workflows_name_project ON Workflows(Name, Project);
            """;
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 21;";
        setVersionCmd.ExecuteNonQuery();
    }
}
