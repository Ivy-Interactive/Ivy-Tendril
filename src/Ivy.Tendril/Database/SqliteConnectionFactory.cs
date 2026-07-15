using Microsoft.Data.Sqlite;

namespace Ivy.Tendril.Database;

/// <summary>
///     Centralizes creation of on-disk SQLite connections so every connection consistently enables
///     WAL journaling and a busy timeout. The Tendril daemon holds a long-lived connection open; CLI
///     commands (doctor, database, run) open their own connections to the same file.
///     <para>
///     Microsoft.Data.Sqlite leaves SQLite's own <c>busy_timeout</c> at 0 by default and instead
///     retries on SQLITE_BUSY at the ADO layer up to the command timeout. That ADO retry does not
///     cover lock escalation inside an explicit write transaction — exactly the path
///     <see cref="Services.Plans.PlanDatabaseService" /> uses — where a contending writer would
///     otherwise surface "database is locked" immediately. Setting <c>PRAGMA busy_timeout</c> makes
///     SQLite's own busy handler wait for the lock to clear on every operation, including those.
///     </para>
/// </summary>
public static class SqliteConnectionFactory
{
    /// <summary>
    ///     How long (ms) a connection waits for a held lock before giving up with SQLITE_BUSY.
    ///     busy_timeout is a per-connection setting, so it must be applied on every connection — which
    ///     is exactly why connection creation is centralized here.
    /// </summary>
    public const int BusyTimeoutMs = 5000;

    /// <summary>
    ///     Opens a SQLite connection to <paramref name="databasePath"/> and applies the standard
    ///     PRAGMAs. busy_timeout is set first so it governs the connection immediately, including the
    ///     WAL journal-mode switch (which itself takes a database lock).
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file.</param>
    /// <param name="readWriteCreate">
    ///     When true (default), opens with <c>Mode=ReadWriteCreate</c> so the file is created if
    ///     missing. Read-only callers can pass false.
    /// </param>
    public static SqliteConnection OpenConfigured(string databasePath, bool readWriteCreate = true)
    {
        var connectionString = readWriteCreate
            ? $"Data Source={databasePath};Mode=ReadWriteCreate"
            : $"Data Source={databasePath}";

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    /// <summary>
    ///     Applies the standard PRAGMAs to an already-open connection. Exposed so callers that need to
    ///     reopen a connection in place (e.g. after corruption recovery) can reuse the same settings.
    /// </summary>
    public static void ApplyPragmas(SqliteConnection connection)
    {
        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText =
            $"PRAGMA busy_timeout={BusyTimeoutMs}; PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragmaCmd.ExecuteNonQuery();
    }
}
