using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_019_JobsTokenBreakdown : IMigration
{
    public int Version => 19;
    public string Description => "Add per-job model and token breakdown (input/output/cache/reasoning) to Jobs";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Jobs ADD COLUMN Model TEXT;
            ALTER TABLE Jobs ADD COLUMN InputTokens INTEGER;
            ALTER TABLE Jobs ADD COLUMN OutputTokens INTEGER;
            ALTER TABLE Jobs ADD COLUMN CacheReadTokens INTEGER;
            ALTER TABLE Jobs ADD COLUMN CacheWriteTokens INTEGER;
            ALTER TABLE Jobs ADD COLUMN ReasoningTokens INTEGER;
            ALTER TABLE Jobs ADD COLUMN CostSource TEXT;
            PRAGMA user_version = 19;
            """;
        cmd.ExecuteNonQuery();
    }
}
