using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_015_RenamePlanStates : IMigration
{
    public int Version => 15;
    public string Description => "Rename plan states Building → Creating and ReadyForReview → Review";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Plans SET State = 'Creating' WHERE State = 'Building';
            UPDATE Plans SET State = 'Review'   WHERE State = 'ReadyForReview';

            PRAGMA user_version = 15;
            """;
        cmd.ExecuteNonQuery();
    }
}
