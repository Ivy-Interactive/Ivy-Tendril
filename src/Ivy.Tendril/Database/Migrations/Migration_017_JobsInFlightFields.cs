using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_017_JobsInFlightFields : IMigration
{
    public int Version => 17;
    public string Description => "Persist in-flight job fields (process id, reported plan and failure reason)";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Jobs ADD COLUMN ProcessId INTEGER;
            ALTER TABLE Jobs ADD COLUMN ReportedPlanId TEXT;
            ALTER TABLE Jobs ADD COLUMN ReportedPlanTitle TEXT;
            ALTER TABLE Jobs ADD COLUMN ReportedFailureReason TEXT;
            PRAGMA user_version = 17;
            """;
        cmd.ExecuteNonQuery();
    }
}
