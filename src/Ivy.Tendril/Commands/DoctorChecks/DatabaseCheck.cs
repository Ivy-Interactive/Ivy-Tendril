using Microsoft.Data.Sqlite;
using Ivy.Tendril.Database;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class DatabaseCheck : IDoctorCheck
{
    public string Name => "Database";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrEmpty(tendrilHome))
        {
            statuses.Add(new CheckStatus("tendril.db", "TENDRIL_HOME not set", StatusKind.Error));
            return new CheckResult(true, statuses);
        }

        var dbPath = Path.Combine(tendrilHome, "tendril.db");
        if (!File.Exists(dbPath))
        {
            statuses.Add(new CheckStatus("tendril.db", "Not found", StatusKind.Error));
            return new CheckResult(true, statuses);
        }

        var fileInfo = new FileInfo(dbPath);
        statuses.Add(new CheckStatus("tendril.db", $"{fileInfo.Length / 1024.0:F0} KB", StatusKind.Ok));

        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragmaCmd.ExecuteNonQuery();

            using var integrityCmd = connection.CreateCommand();
            integrityCmd.CommandText = "PRAGMA integrity_check";
            var integrityResult = integrityCmd.ExecuteScalar()?.ToString();
            if (integrityResult == "ok")
            {
                statuses.Add(new CheckStatus("Integrity", "OK", StatusKind.Ok));
            }
            else
            {
                statuses.Add(new CheckStatus("Integrity", $"FAILED: {integrityResult}", StatusKind.Error));
                hasErrors = true;
            }

            var migrator = new DatabaseMigrator(connection);
            var currentVersion = migrator.GetCurrentVersion();
            var latestVersion = migrator.GetLatestVersion();
            if (currentVersion == latestVersion)
            {
                statuses.Add(new CheckStatus("Schema", $"v{currentVersion} (up to date)", StatusKind.Ok));
            }
            else if (currentVersion < latestVersion)
            {
                statuses.Add(new CheckStatus("Schema", $"v{currentVersion} → v{latestVersion} (needs migration)", StatusKind.Warn));
                hasErrors = true;
            }
            else
            {
                statuses.Add(new CheckStatus("Schema", $"v{currentVersion} (newer than app v{latestVersion})", StatusKind.Warn));
            }
        }
        catch (Exception ex)
        {
            statuses.Add(new CheckStatus("Connection", $"Failed: {ex.Message}", StatusKind.Error));
            hasErrors = true;
        }

        return await Task.FromResult(new CheckResult(hasErrors, statuses));
    }
}
