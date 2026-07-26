using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_023_DeduplicateSystemWorkflows : IMigration
{
    public int Version => 23;
    public string Description => "Delete duplicate project-specific system workflows";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Workflows WHERE IsSystem = 1 AND Project != 'default';
            """;
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 23;";
        setVersionCmd.ExecuteNonQuery();
    }
}
