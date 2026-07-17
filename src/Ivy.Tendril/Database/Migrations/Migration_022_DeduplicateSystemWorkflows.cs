using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_022_DeduplicateSystemWorkflows : IMigration
{
    public int Version => 22;
    public string Description => "Delete duplicate project-specific system workflows";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Workflows WHERE IsSystem = 1 AND Project != 'default';
            """;
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 22;";
        setVersionCmd.ExecuteNonQuery();
    }
}
