using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_016_DropRecommendationRisk : IMigration
{
    public int Version => 16;
    public string Description => "Drop Risk column from Recommendations table";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE Recommendations DROP COLUMN Risk;";
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 16;";
        setVersionCmd.ExecuteNonQuery();
    }
}
