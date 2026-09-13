using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_024_CostsCostSource : IMigration
{
    public int Version => 24;
    public string Description => "Add CostSource column to Costs table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        var hasColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(Costs);";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), "CostSource", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (!hasColumn)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Costs ADD COLUMN CostSource TEXT;";
            alterCmd.ExecuteNonQuery();
        }

        // Populate existing Costs.CostSource where possible by correlating against Jobs
        using (var updateCmd = connection.CreateCommand())
        {
            updateCmd.CommandText = """
                UPDATE Costs
                SET CostSource = (
                    SELECT j.CostSource
                    FROM Jobs j
                    LEFT JOIN Plans p ON p.Id = Costs.PlanId
                    WHERE j.CostSource IS NOT NULL
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
                WHERE CostSource IS NULL;
                """;
            updateCmd.ExecuteNonQuery();
        }

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 24;";
        versionCmd.ExecuteNonQuery();
    }
}
