using Ivy.Tendril.Database;
using Microsoft.Data.Sqlite;

namespace Ivy.Tendril.Test;

public class SqliteConnectionFactoryTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _dbPath;

    public SqliteConnectionFactoryTests()
    {
        _dbPath = Path.Combine(_tempDir.Path, $"factory-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    [Fact]
    public void OpenConfigured_AppliesBusyTimeoutAndWal()
    {
        using var connection = SqliteConnectionFactory.OpenConfigured(_dbPath);

        Assert.Equal(SqliteConnectionFactory.BusyTimeoutMs, ReadPragma<long>(connection, "busy_timeout"));
        Assert.Equal("wal", ReadPragma<string>(connection, "journal_mode"), ignoreCase: true);
    }

    [Fact]
    public async Task ConcurrentWriters_SecondWriterWaitsThenSucceeds()
    {
        // Smoke test: two factory connections contending for the write lock serialize cleanly rather
        // than deadlocking or erroring — writer B waits for A to commit, then its own write lands.
        // (This exercises the WAL + busy_timeout path end-to-end; it is not an isolation test of the
        // pragma, since Microsoft.Data.Sqlite's command-level retry would also absorb a brief wait.)
        CreateTable();

        // Writer A takes the write lock and holds it briefly on a background task.
        using var writerA = SqliteConnectionFactory.OpenConfigured(_dbPath);
        var txA = writerA.BeginTransaction();
        Insert(writerA, txA, 1);

        var release = Task.Run(async () =>
        {
            // Hold far under the 5s busy_timeout so writer B waits and then succeeds.
            await Task.Delay(150);
            txA.Commit();
            writerA.Dispose();
        });

        using (var writerB = SqliteConnectionFactory.OpenConfigured(_dbPath))
        {
            Insert(writerB, transaction: null, value: 2);
        }

        await release;

        Assert.Equal(2, CountRows());
    }

    private void CreateTable()
    {
        using var connection = SqliteConnectionFactory.OpenConfigured(_dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS t (value INTEGER NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction? transaction, int value)
    {
        using var cmd = connection.CreateCommand();
        if (transaction != null)
            cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO t (value) VALUES ($v);";
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private int CountRows()
    {
        using var connection = SqliteConnectionFactory.OpenConfigured(_dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM t;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static T ReadPragma<T>(SqliteConnection connection, string pragma)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {pragma};";
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }
}
