using Ivy.Tendril.Database;
using Microsoft.Data.Sqlite;

namespace Ivy.Tendril.Test;

public class DatabaseFixture : IDisposable
{
    public SqliteConnection Connection { get; }

    public DatabaseFixture()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        using var pragmaCmd = Connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragmaCmd.ExecuteNonQuery();

        var migrator = new DatabaseMigrator(Connection);
        migrator.ApplyMigrations();
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}
