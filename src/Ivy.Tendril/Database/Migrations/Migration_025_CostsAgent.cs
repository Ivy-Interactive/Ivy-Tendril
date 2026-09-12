using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_025_CostsAgent : IMigration
{
    public int Version => 25;
    public string Description => "Add Agent column to Costs table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        var hasColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(Costs);";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), "Agent", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (!hasColumn)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Costs ADD COLUMN Agent TEXT;";
            alterCmd.ExecuteNonQuery();
        }

        // Populate existing Costs.Agent where possible by correlating against Jobs.Provider
        using (var updateCmd = connection.CreateCommand())
        {
            updateCmd.CommandText = """
                UPDATE Costs
                SET Agent = (
                    SELECT j.Provider
                    FROM Jobs j
                    LEFT JOIN Plans p ON p.Id = Costs.PlanId
                    WHERE j.Provider IS NOT NULL
                      AND j.Type = Costs.Promptware
                      AND (
                          j.ReportedPlanId = CAST(Costs.PlanId AS TEXT)
                          OR (p.FolderPath IS NOT NULL AND j.PlanFile = p.FolderPath)
                          OR (p.FolderName IS NOT NULL AND j.PlanFile = p.FolderName)
                          OR j.PlanFile LIKE '%' || PRINTF('%05d', Costs.PlanId) || '%'
                      )
                    ORDER BY j.CompletedAt DESC
                    LIMIT 1
                )
                WHERE Agent IS NULL;
                """;
            updateCmd.ExecuteNonQuery();
        }

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 25;";
        versionCmd.ExecuteNonQuery();
    }
}
