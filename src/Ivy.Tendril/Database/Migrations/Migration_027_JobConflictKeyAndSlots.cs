using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

/// <summary>
///     Two things the job admission path needs to see across process boundaries. <c>ConflictKey</c>
///     makes the dedup guard visible to another instance: it used to scan an in-process dictionary
///     only, so 3-4 instances over one TENDRIL_HOME each admitted the same submission (#2710).
///     <c>JobSlots</c> makes the concurrency cap machine wide: <c>maxConcurrentJobs: 30</c> per process
///     meant up to 120 concurrent agents, and the OOM killer collected the difference.
/// </summary>
public class Migration_027_JobConflictKeyAndSlots : IMigration
{
    public int Version => 27;
    public string Description => "Add Jobs.ConflictKey and the machine wide JobSlots lease table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        AddConflictKeyIfMissing(connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_jobs_conflictkey ON Jobs(ConflictKey);
            CREATE TABLE IF NOT EXISTS JobSlots (
                JobId       TEXT PRIMARY KEY,
                OwnerPid    INTEGER NOT NULL,
                AgentPid    INTEGER,
                MachineName TEXT NOT NULL,
                AcquiredAt  TEXT NOT NULL,
                Heartbeat   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_jobslots_heartbeat ON JobSlots(Heartbeat);
            """;
        cmd.ExecuteNonQuery();

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 27;";
        versionCmd.ExecuteNonQuery();
    }

    // Existing rows keep ConflictKey NULL. No backfill: only live jobs matter to the guard, and a null
    // never matches a computed key.
    private static void AddConflictKeyIfMissing(SqliteConnection connection)
    {
        var hasColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(Jobs);";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), "ConflictKey", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn)
            return;

        using var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = "ALTER TABLE Jobs ADD COLUMN ConflictKey TEXT;";
        alterCmd.ExecuteNonQuery();
    }
}
