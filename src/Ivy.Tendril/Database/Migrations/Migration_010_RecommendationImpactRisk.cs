using Microsoft.Data.Sqlite;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_010_RecommendationImpactRisk : IMigration
{
    public int Version => 10;
    public string Description => "Add Impact and Risk columns to Recommendations table";

    public void Apply(SqliteConnection connection)
    {
        using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = "ALTER TABLE Recommendations ADD COLUMN Impact TEXT;";
        cmd1.ExecuteNonQuery();

        using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = "ALTER TABLE Recommendations ADD COLUMN Risk TEXT;";
        cmd2.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 10;";
        setVersionCmd.ExecuteNonQuery();
    }
}
