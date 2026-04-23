using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Database;

public class DatabaseMigrator
{
    private readonly SqliteConnection _connection;
    private readonly ILogger _logger;
    private readonly List<IMigration> _migrations;

    public DatabaseMigrator(SqliteConnection connection, ILogger? logger = null)
    {
        _connection = connection;
        _logger = logger ?? NullLogger<DatabaseMigrator>.Instance;
        _migrations = LoadMigrations();
    }

    internal DatabaseMigrator(SqliteConnection connection, List<IMigration> migrations, ILogger? logger = null)
    {
        _connection = connection;
        _logger = logger ?? NullLogger<DatabaseMigrator>.Instance;
        _migrations = migrations;
        ValidateMigrationSequence(_migrations);
    }

    public int GetCurrentVersion()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    public int GetLatestVersion()
    {
        return _migrations.Count > 0 ? _migrations.Max(m => m.Version) : 0;
    }

    public void ApplyMigrations()
    {
        var currentVersion = GetCurrentVersion();
        var latestVersion = GetLatestVersion();

        if (currentVersion == latestVersion)
        {
            _logger.LogInformation("Database is up to date (version {CurrentVersion})", currentVersion);
            return;
        }

        if (currentVersion > latestVersion)
            throw new InvalidOperationException(
                $"Database version ({currentVersion}) is newer than application version ({latestVersion}). " +
                "Please update the application.");

        _logger.LogInformation("Migrating database from version {CurrentVersion} to {LatestVersion}...", currentVersion, latestVersion);

        var pendingMigrations = _migrations
            .Where(m => m.Version > currentVersion)
            .OrderBy(m => m.Version)
            .ToList();

        using var transaction = _connection.BeginTransaction();

        try
        {
            foreach (var migration in pendingMigrations)
            {
                _logger.LogInformation("  Applying migration {MigrationVersion}: {MigrationDescription}", migration.Version, migration.Description);
                migration.Apply(_connection, _logger);

                var newVersion = GetCurrentVersion();
                if (newVersion != migration.Version)
                    throw new InvalidOperationException(
                        $"Migration {migration.Version} did not set PRAGMA user_version correctly. " +
                        $"Expected {migration.Version}, got {newVersion}");
            }

            transaction.Commit();
            _logger.LogInformation("Migration complete. Database is now at version {LatestVersion}", latestVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError("Migration failed: {ErrorMessage}", ex.Message);
            transaction.Rollback();
            throw;
        }
    }

    private static List<IMigration> LoadMigrations()
    {
        var migrationType = typeof(IMigration);
        var migrations = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => migrationType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IMigration)Activator.CreateInstance(t)!)
            .OrderBy(m => m.Version)
            .ToList();

        ValidateMigrationSequence(migrations);

        return migrations;
    }

    private static void ValidateMigrationSequence(List<IMigration> migrations)
    {
        if (migrations.Count == 0) return;

        var duplicates = migrations.GroupBy(m => m.Version)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate migration versions found: {string.Join(", ", duplicates)}");

        for (var i = 0; i < migrations.Count; i++)
        {
            var expected = i + 1;
            var actual = migrations[i].Version;

            if (actual != expected)
                throw new InvalidOperationException(
                    $"Migration sequence is invalid. Expected version {expected}, found {actual}. " +
                    "Migrations must be numbered sequentially starting from 1.");
        }
    }
}