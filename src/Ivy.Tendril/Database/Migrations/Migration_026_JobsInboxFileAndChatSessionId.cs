using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

/// <summary>
///     <c>JobItem.InboxFile</c> was memory only, so the guard that stops an inbox file being picked up
///     twice could never fire for a job the process had not started itself (#2710). Persisting it
///     makes a breadcrumb resolvable back to its job across a restart. <c>ChatSessionId</c> only
///     survived inside the <c>TypedArgs</c> JSON blob; a column makes it queryable and independent of
///     that blob.
/// </summary>
public class Migration_026_JobsInboxFileAndChatSessionId : IMigration
{
    public int Version => 26;
    public string Description => "Add InboxFile and ChatSessionId columns to Jobs table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        AddColumnIfMissing(connection, "InboxFile");
        AddColumnIfMissing(connection, "ChatSessionId");

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 26;";
        versionCmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string column)
    {
        var hasColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(Jobs);";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn)
            return;

        using var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE Jobs ADD COLUMN {column} TEXT;";
        alterCmd.ExecuteNonQuery();
    }
}
