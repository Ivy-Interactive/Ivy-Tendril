using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_018_JobsRateLimit : IMigration
{
    public int Version => 18;

    public string Description =>
        "Add RateLimitedUntil and RateLimitRetries columns to Jobs so a rate-limit cooldown survives a restart";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Jobs ADD COLUMN RateLimitedUntil TEXT;
            ALTER TABLE Jobs ADD COLUMN RateLimitRetries INTEGER NOT NULL DEFAULT 0;
            PRAGMA user_version = 18;
            """;
        cmd.ExecuteNonQuery();
    }
}
