using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_018_PrStatusBranch : IMigration
{
    public int Version => 18;
    public string Description => "Add branch (head ref) to the PR status cache";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE PrStatuses ADD COLUMN Branch TEXT;
            PRAGMA user_version = 18;
            """;
        cmd.ExecuteNonQuery();
    }
}
