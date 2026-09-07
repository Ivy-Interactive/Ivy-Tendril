using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_023_PlanChatSessionId : IMigration
{
    public int Version => 23;
    public string Description => "Add ChatSessionId column to Plans table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        var hasColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(Plans);";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), "ChatSessionId", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (!hasColumn)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Plans ADD COLUMN ChatSessionId TEXT;";
            alterCmd.ExecuteNonQuery();
        }

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 23;";
        versionCmd.ExecuteNonQuery();
    }
}
