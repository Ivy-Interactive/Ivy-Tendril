using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Database;

public static class DatabaseCommands
{
    public static int DbVersionInternal(string dbPath, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        using var connection = OpenConnection(dbPath);
        var migrator = new DatabaseMigrator(connection);
        var current = migrator.GetCurrentVersion();
        var latest = migrator.GetLatestVersion();
        var status = current == latest ? "Up to date"
            : current > latest ? "Newer than application"
            : "Needs migration";

        logger.LogInformation("Database version: {CurrentVersion}", current);
        logger.LogInformation("Latest version:   {LatestVersion}", latest);
        logger.LogInformation("Status:           {Status}", status);
        return 0;
    }

    public static int DbMigrateInternal(string dbPath, ILogger? logger = null)
    {
        using var connection = OpenConnection(dbPath);
        var migrator = new DatabaseMigrator(connection, logger);
        migrator.ApplyMigrations();
        return 0;
    }

    public static int DbResetInternal(string dbPath, bool force, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        if (!force)
        {
            // Interactive UI prompt - keep as Console
            Console.Write("WARNING: This will delete all data in the database. Are you sure? [y/n] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y")
            {
                // Interactive UI feedback - keep as Console
                Console.WriteLine("Aborted.");
                return 1;
            }
        }

        logger.LogInformation("Resetting database...");

        using var connection = OpenConnection(dbPath);

        // Get all table names
        var tables = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name != 'sqlite_sequence';";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        // Drop all tables
        logger.LogInformation("  Dropping existing tables");
        foreach (var table in tables)
        {
            using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = $"DROP TABLE IF EXISTS \"{table}\";";
            dropCmd.ExecuteNonQuery();
        }

        // Reset version
        using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "PRAGMA user_version = 0;";
            versionCmd.ExecuteNonQuery();
        }

        // Re-apply all migrations
        var migrator = new DatabaseMigrator(connection);
        migrator.ApplyMigrations();
        return 0;
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragmaCmd.ExecuteNonQuery();
        return connection;
    }
}