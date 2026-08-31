using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

/// <summary>
/// Records the reasoning effort a job ran at, next to the execution profile added by
/// <see cref="Migration_020_JobsExecutionProfile" />. The profile alone does not determine it: an
/// effort can be set per agent profile and overridden per promptware, so the run's own value is
/// the only reliable record of what it actually used.
/// </summary>
public class Migration_021_JobsEffort : IMigration
{
    public int Version => 21;
    public string Description => "Add Effort to Jobs";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          ALTER TABLE Jobs ADD COLUMN Effort TEXT;
                          PRAGMA user_version = 21;
                          """;
        cmd.ExecuteNonQuery();
    }
}
