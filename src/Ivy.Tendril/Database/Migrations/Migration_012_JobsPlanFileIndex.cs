using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_012_JobsPlanFileIndex : IMigration
{
    public int Version => 12;
    public string Description => "Add index on Jobs.PlanFile for plan-specific queries";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_jobs_planfile ON Jobs(PlanFile);
            PRAGMA user_version = 12;
            """;
        cmd.ExecuteNonQuery();
    }
}
