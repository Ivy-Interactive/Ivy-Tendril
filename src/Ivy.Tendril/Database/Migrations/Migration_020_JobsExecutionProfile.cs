using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

/// <summary>
/// Records the execution profile a job ran under, so the Jobs list and the cost sheet can report it
/// without re-reading the plan — which would show the plan's <em>current</em> profile, not the one
/// that was in force when the job launched.
/// </summary>
public class Migration_020_JobsExecutionProfile : IMigration
{
    public int Version => 20;
    public string Description => "Add ExecutionProfile to Jobs";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          ALTER TABLE Jobs ADD COLUMN ExecutionProfile TEXT;
                          PRAGMA user_version = 20;
                          """;
        cmd.ExecuteNonQuery();
    }
}
