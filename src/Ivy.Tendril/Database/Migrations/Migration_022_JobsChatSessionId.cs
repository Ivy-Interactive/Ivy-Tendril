using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

/// <summary>
/// Records the originating chat session identifier for jobs launched from the chat application.
/// </summary>
public class Migration_022_JobsChatSessionId : IMigration
{
    public int Version => 22;
    public string Description => "Add ChatSessionId to Jobs";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          ALTER TABLE Jobs ADD COLUMN ChatSessionId TEXT;
                          PRAGMA user_version = 22;
                          """;
        cmd.ExecuteNonQuery();
    }
}
