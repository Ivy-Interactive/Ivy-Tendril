using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

/// <summary>
/// Lets a cost be unknown. A subscription plan such as Claude Max reports tokens and no charge, and
/// writing that as 0.0 made an unpriceable plan look free: it dragged every average down and hid the
/// spend entirely. NULL is the representation because SQL SUM and COUNT(column) both skip it, which
/// is exactly the arithmetic "average over the plans we could price" needs.
/// <para>
/// Model comes along because costs.csv is the durable record of what the tokens went on once
/// PurgeOldJobs has dropped the Jobs row.
/// </para>
/// <para>
/// SQLite cannot drop NOT NULL with ALTER TABLE, so the table is rebuilt. No foreign_keys pragma is
/// emitted: <see cref="DatabaseMigrator" /> runs every pending migration inside one transaction,
/// where the pragma is a silent no op, and the rebuild is safe under enforcement regardless since
/// nothing references Costs and the copied rows already satisfy the Plans key. DROP TABLE takes the
/// table's indexes with it, so idx_costs_plan_logtimestamp has to be recreated.
/// </para>
/// <para>
/// Existing zeros stay zeros. Which historical 0.0 meant "unknown" is not guessable from the row;
/// <c>CostBackfillService</c> repairs them from the job's token counts instead.
/// </para>
/// </summary>
public class Migration_022_CostsNullableCostAndModel : IMigration
{
    public int Version => 22;
    public string Description => "Allow NULL Costs.Cost and add Costs.Model";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          CREATE TABLE Costs_new (
                              Id INTEGER PRIMARY KEY AUTOINCREMENT,
                              PlanId INTEGER NOT NULL,
                              Promptware TEXT NOT NULL,
                              Tokens INTEGER NOT NULL,
                              Cost REAL NULL,
                              Model TEXT,
                              LogTimestamp TEXT,
                              FOREIGN KEY (PlanId) REFERENCES Plans(Id) ON DELETE CASCADE);
                          INSERT INTO Costs_new (Id, PlanId, Promptware, Tokens, Cost, LogTimestamp)
                              SELECT Id, PlanId, Promptware, Tokens, Cost, LogTimestamp FROM Costs;
                          DROP TABLE Costs;
                          ALTER TABLE Costs_new RENAME TO Costs;
                          CREATE INDEX IF NOT EXISTS idx_costs_plan_logtimestamp ON Costs(PlanId, LogTimestamp);
                          PRAGMA user_version = 22;
                          """;
        cmd.ExecuteNonQuery();
    }
}
