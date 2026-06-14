using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_012_SourceIdentifier : IMigration
{
    public int Version => 12;
    public string Description => "Add SourceIdentifier column to Plans table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Plans ADD COLUMN SourceIdentifier TEXT;

            PRAGMA user_version = 12;
            """;
        cmd.ExecuteNonQuery();
    }
}
