using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_023_PlanChatSessionId : IMigration
{
    public int Version => 23;
    public string Description => "Add ChatSessionId column to Plans table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Plans ADD COLUMN ChatSessionId TEXT;

            PRAGMA user_version = 23;
            """;
        cmd.ExecuteNonQuery();
    }
}
