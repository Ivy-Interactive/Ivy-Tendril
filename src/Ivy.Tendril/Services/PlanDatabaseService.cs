using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class PlanDatabaseService : IPlanDatabaseService
{
    private static readonly HashSet<string> AllowedTableColumns = new(StringComparer.Ordinal)
    {
        "Repos", "RepoPath", "Commits", "CommitHash", "PullRequests", "PrUrl",
        "RelatedPlans", "RelatedPlanPath", "DependsOn", "DependsOnPlanPath", "Verifications"
    };

    private readonly SqliteConnection _connection;
    private readonly ILogger<PlanDatabaseService> _logger;
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    private sealed class ReadLockHandle : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public ReadLockHandle(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterReadLock();
        }
        public void Dispose() => _lock.ExitReadLock();
    }

    private sealed class WriteLockHandle : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public WriteLockHandle(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterWriteLock();
        }
        public void Dispose() => _lock.ExitWriteLock();
    }

    public PlanDatabaseService(string databasePath, ILogger<PlanDatabaseService> logger)
    {
        _logger = logger;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate");
        _connection.Open();

        // Check database integrity before use
        var isCorrupted = false;
        try
        {
            using var integrityCmd = _connection.CreateCommand();
            integrityCmd.CommandText = "PRAGMA integrity_check";
            var integrityResult = integrityCmd.ExecuteScalar()?.ToString();
            isCorrupted = integrityResult != "ok";
        }
        catch (SqliteException)
        {
            isCorrupted = true;
        }

        if (isCorrupted)
        {
            _logger.LogWarning("Database corruption detected, recreating: {Path}", databasePath);
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();
            File.Delete(databasePath);
            if (File.Exists(databasePath + "-wal"))
                File.Delete(databasePath + "-wal");
            if (File.Exists(databasePath + "-shm"))
                File.Delete(databasePath + "-shm");

            // Reopen clean database
            _connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate");
            _connection.Open();
        }

        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragmaCmd.ExecuteNonQuery();

        var migrator = new DatabaseMigrator(_connection);
        migrator.ApplyMigrations();
        _logger.LogInformation("Database migrations applied");
    }

    public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
    {
        using (new ReadLockHandle(_lock))
        {
            var sql = """
                      SELECT Id, Title, Project, Level, State, FolderPath, FolderName,
                             YamlRaw, RevisionCount, LatestRevisionContent, Created, Updated, InitialPrompt, SourceUrl
                      FROM Plans
                      """;

            if (statusFilter.HasValue)
                sql += " WHERE State = @state";

            sql += " ORDER BY Id";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;

            if (statusFilter.HasValue)
                cmd.Parameters.AddWithValue("@state", statusFilter.Value.ToString());

            var planIds = new List<int>();
            var plans = new List<PlanFile>();

            using var reader = cmd.ExecuteReader();
            var rawPlans = new List<(int Id, string Title, string Project, string Level, string State,
                string FolderPath, string FolderName, string YamlRaw, int RevisionCount,
                string LatestContent, string Created, string Updated, string? InitialPrompt, string? SourceUrl)>();

            PlanRowOrdinals? ordinals = null;
            while (reader.Read())
            {
                ordinals ??= GetPlanRowOrdinals(reader);
                var row = ReadPlanRow(reader, ordinals.Value);
                planIds.Add(row.Id);
                rawPlans.Add(row);
            }

            if (rawPlans.Count == 0)
                return plans;

            var allRepos = BatchGetList(planIds, "Repos", "RepoPath");
            var allCommits = BatchGetList(planIds, "Commits", "CommitHash");
            var allPrs = BatchGetList(planIds, "PullRequests", "PrUrl");
            var allVerifications = BatchGetVerifications(planIds);
            var allRelatedPlans = BatchGetList(planIds, "RelatedPlans", "RelatedPlanPath");
            var allDependsOn = BatchGetList(planIds, "DependsOn", "DependsOnPlanPath");

            foreach (var row in rawPlans)
            {
                var plan = BuildPlanFileFromRow(row.Id, row.Title, row.Project, row.Level, row.State,
                    row.FolderPath, row.FolderName, row.YamlRaw, row.RevisionCount, row.LatestContent,
                    row.Created, row.Updated, row.InitialPrompt, row.SourceUrl,
                    allRepos.GetValueOrDefault(row.Id, []),
                    allCommits.GetValueOrDefault(row.Id, []),
                    allPrs.GetValueOrDefault(row.Id, []),
                    allVerifications.GetValueOrDefault(row.Id, []),
                    allRelatedPlans.GetValueOrDefault(row.Id, []),
                    allDependsOn.GetValueOrDefault(row.Id, []));
                if (plan != null)
                    plans.Add(plan);
            }

            return plans;
        }
    }

    public PlanFile? GetPlanByFolder(string folderPath)
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              SELECT Id, Title, Project, Level, State, FolderPath, FolderName,
                                     YamlRaw, RevisionCount, LatestRevisionContent, Created, Updated, InitialPrompt, SourceUrl
                              FROM Plans WHERE FolderPath = @folderPath
                              """;
            cmd.Parameters.AddWithValue("@folderPath", folderPath);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return BuildPlanFile(reader);

            return null;
        }
    }

    public PlanFile? GetPlanById(int planId)
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              SELECT Id, Title, Project, Level, State, FolderPath, FolderName,
                                     YamlRaw, RevisionCount, LatestRevisionContent, Created, Updated, InitialPrompt, SourceUrl
                              FROM Plans WHERE Id = @id
                              """;
            cmd.Parameters.AddWithValue("@id", planId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return BuildPlanFile(reader);

            return null;
        }
    }

    public PlanReaderService.PlanCountSnapshot ComputePlanCounts()
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  COALESCE(SUM(CASE WHEN State IN ('Draft', 'Blocked') THEN 1 ELSE 0 END), 0) AS DraftCount,
                                  COALESCE(SUM(CASE WHEN State = 'ReadyForReview' THEN 1 ELSE 0 END), 0) AS ReadyForReviewCount,
                                  COALESCE(SUM(CASE WHEN State = 'Failed' THEN 1 ELSE 0 END), 0) AS FailedCount,
                                  COALESCE(SUM(CASE WHEN State = 'Icebox' THEN 1 ELSE 0 END), 0) AS IceboxCount,
                                  (SELECT COUNT(*) FROM Recommendations WHERE State = 'Pending') AS PendingRecommendationsCount,
                                  COUNT(*) AS TotalPlans
                              FROM Plans
                              """;

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var draftOrdinal = reader.GetOrdinal("DraftCount");
                var readyForReviewOrdinal = reader.GetOrdinal("ReadyForReviewCount");
                var failedOrdinal = reader.GetOrdinal("FailedCount");
                var iceboxOrdinal = reader.GetOrdinal("IceboxCount");
                var pendingRecsOrdinal = reader.GetOrdinal("PendingRecommendationsCount");
                var totalOrdinal = reader.GetOrdinal("TotalPlans");

                return new PlanReaderService.PlanCountSnapshot(
                    reader.GetInt32(draftOrdinal),
                    reader.GetInt32(readyForReviewOrdinal),
                    reader.GetInt32(failedOrdinal),
                    reader.GetInt32(iceboxOrdinal),
                    reader.GetInt32(pendingRecsOrdinal),
                    reader.GetInt32(totalOrdinal)
                );
            }

            return new PlanReaderService.PlanCountSnapshot(0, 0, 0, 0, 0, 0);
        }
    }

    public DashboardModels GetDashboardData(string? projectFilter)
    {
        using (new ReadLockHandle(_lock))
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-6).ToString("yyyy-MM-dd");
            var pf = projectFilter != null ? " AND Project = @project" : "";
            var pfAlias = projectFilter != null ? " AND p.Project = @project" : "";
            var pfAlias2 = projectFilter != null ? " AND p2.Project = @project2" : "";

            // Query 1: Status counts + avg cost (LAST 7 DAYS - filtered by cutoff)
            int totalCount, draftCount, inProgressCount, reviewCount, completedCount, failedCount;
            decimal avgCost;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT
                        COUNT(*) AS TotalCount,
                        COALESCE(SUM(CASE WHEN State IN ('Draft', 'Blocked') THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State IN ('Building', 'Executing', 'Updating') THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State = 'ReadyForReview' THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State = 'Completed' THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State = 'Failed' THEN 1 ELSE 0 END), 0),
                        (SELECT CASE WHEN COUNT(DISTINCT p2.Id) > 0
                            THEN COALESCE(SUM(c2.Cost), 0) / COUNT(DISTINCT p2.Id) ELSE 0 END
                         FROM Costs c2 JOIN Plans p2 ON p2.Id = c2.PlanId
                         WHERE p2.Created >= @cutoff AND p2.State IN ('Completed', 'Failed', 'ReadyForReview') {pfAlias2}
                        ) AS AvgCost
                    FROM Plans WHERE Created >= @cutoff {pf}
                    """;
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                if (projectFilter != null)
                {
                    cmd.Parameters.AddWithValue("@project", projectFilter);
                    cmd.Parameters.AddWithValue("@project2", projectFilter);
                }

                using var r = cmd.ExecuteReader();
                r.Read();
                totalCount = r.GetInt32(0);
                draftCount = r.GetInt32(1);
                inProgressCount = r.GetInt32(2);
                reviewCount = r.GetInt32(3);
                completedCount = r.GetInt32(4);
                failedCount = r.GetInt32(5);
                avgCost = Convert.ToDecimal(r.GetValue(6), CultureInfo.InvariantCulture);
            }

            // Query 2: All daily stats in one pass (LAST 7 DAYS - uses cutoff)
            var dailyCreated = new Dictionary<string, int>();
            var dailyCompleted = new Dictionary<string, int>();
            var dailyFailed = new Dictionary<string, int>();
            var dailyPrs = new Dictionary<string, int>();
            var dailyCosts = new Dictionary<string, decimal>();
            var dailyTokens = new Dictionary<string, int>();

            using (var cmd = _connection.CreateCommand())
            {
                // Build day list for IN clause
                var days = new List<string>();
                for (var i = 0; i < 7; i++)
                    days.Add(DateTime.UtcNow.Date.AddDays(-i).ToString("yyyy-MM-dd"));

                cmd.CommandText = $"""
                    WITH cte_created AS (
                        SELECT DATE(Created) AS d, COUNT(*) AS cnt FROM Plans
                        WHERE Created >= @cutoff {pf} GROUP BY DATE(Created)
                    ),
                    cte_completed_failed AS (
                        SELECT DATE(Updated) AS d, State, COUNT(*) AS cnt FROM Plans
                        WHERE Updated >= @cutoff AND State IN ('Completed', 'Failed') {pf}
                        GROUP BY DATE(Updated), State
                    ),
                    cte_prs AS (
                        SELECT DATE(p.Updated) AS d, COUNT(*) AS cnt
                        FROM PullRequests pr JOIN Plans p ON p.Id = pr.PlanId
                        WHERE p.Updated >= @cutoff AND p.State = 'Completed' {pfAlias}
                        GROUP BY DATE(p.Updated)
                    ),
                    cte_costs AS (
                        SELECT DATE(p.Updated) AS d, SUM(c.Cost) AS cost, SUM(c.Tokens) AS tokens
                        FROM Costs c JOIN Plans p ON p.Id = c.PlanId
                        WHERE p.Updated >= @cutoff AND p.State IN ('Completed', 'Failed', 'ReadyForReview') {pfAlias}
                        GROUP BY DATE(p.Updated)
                    ),
                    cte_days(day) AS (
                        VALUES {string.Join(",", days.Select((_, idx) => $"(@day{idx})"))}
                    )
                    SELECT
                        cte_days.day,
                        COALESCE(cr.cnt, 0) AS Created,
                        COALESCE(co.cnt, 0) AS Completed,
                        COALESCE(pr.cnt, 0) AS PrsMerged,
                        COALESCE(fa.cnt, 0) AS Failed,
                        COALESCE(cs.cost, 0) AS Cost,
                        COALESCE(cs.tokens, 0) AS Tokens
                    FROM cte_days
                    LEFT JOIN cte_created cr ON cr.d = cte_days.day
                    LEFT JOIN cte_completed_failed co ON co.d = cte_days.day AND co.State = 'Completed'
                    LEFT JOIN cte_completed_failed fa ON fa.d = cte_days.day AND fa.State = 'Failed'
                    LEFT JOIN cte_prs pr ON pr.d = cte_days.day
                    LEFT JOIN cte_costs cs ON cs.d = cte_days.day
                    ORDER BY cte_days.day DESC
                    """;

                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                if (projectFilter != null)
                {
                    cmd.Parameters.AddWithValue("@project", projectFilter);
                }
                for (var i = 0; i < days.Count; i++)
                    cmd.Parameters.AddWithValue($"@day{i}", days[i]);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var day = r.GetString(0);
                    dailyCreated[day] = r.GetInt32(1);
                    dailyCompleted[day] = r.GetInt32(2);
                    dailyPrs[day] = r.GetInt32(3);
                    dailyFailed[day] = r.GetInt32(4);
                    dailyCosts[day] = Convert.ToDecimal(r.GetValue(5), CultureInfo.InvariantCulture);
                    dailyTokens[day] = Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture);
                }
            }

            // Build daily stats for last 7 days
            var dailyStats = new List<DashboardDayStats>();
            for (var i = 0; i < 7; i++)
            {
                var day = DateTime.UtcNow.Date.AddDays(-i);
                var key = day.ToString("yyyy-MM-dd");
                dailyStats.Add(new DashboardDayStats(
                    day,
                    dailyCreated.GetValueOrDefault(key),
                    dailyCompleted.GetValueOrDefault(key),
                    dailyPrs.GetValueOrDefault(key),
                    dailyFailed.GetValueOrDefault(key),
                    dailyCosts.GetValueOrDefault(key),
                    dailyTokens.GetValueOrDefault(key)
                ));
            }

            // Query 3: Project counts (LAST 7 DAYS - filtered by cutoff)
            var projectCounts = new List<ProjectCount>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Project, COUNT(*) FROM Plans WHERE Created >= @cutoff GROUP BY Project ORDER BY COUNT(*) DESC";
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    projectCounts.Add(new ProjectCount(r.GetString(0), r.GetInt32(1)));
            }

            return new DashboardModels(
                totalCount, draftCount, inProgressCount, reviewCount, completedCount, failedCount,
                avgCost, dailyStats, projectCounts);
        }
    }

    public decimal GetPlanTotalCost(int planId)
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Cost), 0) FROM Costs WHERE PlanId = @planId";
            cmd.Parameters.AddWithValue("@planId", planId);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDecimal(result, CultureInfo.InvariantCulture) : 0m;
        }
    }

    public int GetPlanTotalTokens(int planId)
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Tokens), 0) FROM Costs WHERE PlanId = @planId";
            cmd.Parameters.AddWithValue("@planId", planId);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result, CultureInfo.InvariantCulture) : 0;
        }
    }

    public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null)
    {
        using (new ReadLockHandle(_lock))
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);

            using var cmd = _connection.CreateCommand();
            var projectClause = projectFilter != null ? " AND p.Project = @project" : "";
            cmd.CommandText = $"""
                               SELECT
                                   strftime('%Y-%m-%d %H:00:00', COALESCE(c.LogTimestamp, p.Updated)) as Hour,
                                   p.Project,
                                   SUM(c.Cost) as TotalCost,
                                   SUM(c.Tokens) as TotalTokens
                               FROM Costs c
                               JOIN Plans p ON p.Id = c.PlanId
                               WHERE COALESCE(c.LogTimestamp, p.Updated) >= @cutoff{projectClause}
                               GROUP BY Hour, p.Project
                               ORDER BY Hour, p.Project
                               """;
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
            if (projectFilter != null)
            {
                cmd.Parameters.AddWithValue("@project", projectFilter);
            }

            var result = new List<HourlyTokenBurn>();
            using var reader = cmd.ExecuteReader();
            int hourOrdinal = -1, projectOrdinal = -1, totalCostOrdinal = -1, totalTokensOrdinal = -1;
            while (reader.Read())
            {
                if (hourOrdinal == -1)
                {
                    hourOrdinal = reader.GetOrdinal("Hour");
                    projectOrdinal = reader.GetOrdinal("Project");
                    totalCostOrdinal = reader.GetOrdinal("TotalCost");
                    totalTokensOrdinal = reader.GetOrdinal("TotalTokens");
                }

                var hourStr = reader.GetString(hourOrdinal);
                if (DateTime.TryParse(hourStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal,
                        out var hour))
                    result.Add(new HourlyTokenBurn
                    {
                        Hour = DateTime.SpecifyKind(hour, DateTimeKind.Utc),
                        Project = reader.GetString(projectOrdinal),
                        Cost = Convert.ToDecimal(reader.GetDouble(totalCostOrdinal), CultureInfo.InvariantCulture),
                        Tokens = reader.GetInt32(totalTokensOrdinal)
                    });
            }

            return result;
        }
    }

    public List<Recommendation> GetRecommendations()
    {
        using (new ReadLockHandle(_lock))
        {
            return ReadList("""
                            SELECT r.Title, r.Description, r.State, r.PlanId, r.PlanTitle,
                                   r.PlanFolderName, r.Project, r.Date, r.SourcePlanStatus, r.DeclineReason,
                                   r.Impact, r.Risk
                            FROM Recommendations r
                            ORDER BY r.Date DESC
                            """,
                reader =>
                {
                    var sourcePlanStatusStr = reader.GetString(reader.GetOrdinal("SourcePlanStatus"));
                    if (!Enum.TryParse<PlanStatus>(sourcePlanStatusStr, true, out var sourceStatus))
                        sourceStatus = PlanStatus.Draft;

                    return new Recommendation(
                        reader.GetString(reader.GetOrdinal("Title")),
                        reader.GetString(reader.GetOrdinal("Description")),
                        reader.GetString(reader.GetOrdinal("State")),
                        reader.GetInt32(reader.GetOrdinal("PlanId")).ToString("D5"),
                        reader.GetString(reader.GetOrdinal("PlanTitle")),
                        reader.GetString(reader.GetOrdinal("PlanFolderName")),
                        reader.GetString(reader.GetOrdinal("Project")),
                        DateTime.Parse(reader.GetString(reader.GetOrdinal("Date")), CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal),
                        sourceStatus,
                        reader.GetStringOrNull(reader.GetOrdinal("DeclineReason")),
                        reader.GetStringOrNull(reader.GetOrdinal("Impact")),
                        reader.GetStringOrNull(reader.GetOrdinal("Risk"))
                    );
                });
        }
    }

    public int GetPendingRecommendationsCount()
    {
        using (new ReadLockHandle(_lock))
        {
            return ExecuteScalar<int>("SELECT COUNT(*) FROM Recommendations WHERE State = 'Pending'");
        }
    }

    internal static string SanitizeFts5Query(string query)
    {
        // Replace path separators with spaces
        var sanitized = query.Replace('/', ' ').Replace('\\', ' ');

        // Remove grouping and prefix chars that cause parse errors
        sanitized = sanitized.Replace("(", "").Replace(")", "").Replace("^", "");

        // Remove bare asterisks (keep prefix wildcards like "button*")
        sanitized = Regex.Replace(sanitized, @"(?<!\w)\*", "");

        // Balance double quotes: if odd count, replace with spaces to separate adjacent words
        if (sanitized.Count(c => c == '"') % 2 != 0)
            sanitized = sanitized.Replace("\"", " ");

        // Collapse whitespace and trim
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

        return sanitized;
    }

    public List<PlanFile> SearchPlans(string query)
    {
        using (new ReadLockHandle(_lock))
        {
            // Try FTS5 first with sanitized query
            var sanitizedQuery = SanitizeFts5Query(query);

            List<PlanFile> plans = [];

            if (!string.IsNullOrWhiteSpace(sanitizedQuery))
            {
                using var ftsCmd = _connection.CreateCommand();
                ftsCmd.CommandText = """
                                     SELECT p.Id, p.Title, p.Project, p.Level, p.State, p.FolderPath, p.FolderName,
                                            p.YamlRaw, p.RevisionCount, p.LatestRevisionContent, p.Created, p.Updated, p.InitialPrompt, p.SourceUrl
                                     FROM Plans p
                                     INNER JOIN PlanSearch fts ON fts.rowid = p.Id
                                     WHERE PlanSearch MATCH @query
                                     ORDER BY rank, p.Id
                                     """;
                ftsCmd.Parameters.AddWithValue("@query", sanitizedQuery);

                try
                {
                    plans = ExecuteSearchQuery(ftsCmd);
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex)
                {
                    _logger?.LogDebug(ex, "FTS5 query syntax error for query '{Query}', falling back to LIKE", sanitizedQuery);
                    // FTS5 syntax error — safety net for edge cases the sanitizer doesn't handle
                    plans = [];
                }
            }

            // If FTS5 returns no results, fall back to LIKE for substring matching
            if (plans.Count == 0)
            {
                var search = $"%{query}%";
                using var likeCmd = _connection.CreateCommand();
                likeCmd.CommandText = """
                                      SELECT Id, Title, Project, Level, State, FolderPath, FolderName,
                                             YamlRaw, RevisionCount, LatestRevisionContent, Created, Updated, InitialPrompt, SourceUrl
                                      FROM Plans
                                      WHERE Title LIKE @search OR LatestRevisionContent LIKE @search
                                            OR CAST(Id AS TEXT) LIKE @search OR Project LIKE @search
                                            OR SourceUrl LIKE @search OR InitialPrompt LIKE @search
                                      ORDER BY Id
                                      """;
                likeCmd.Parameters.AddWithValue("@search", search);
                plans = ExecuteSearchQuery(likeCmd);
            }

            return plans;
        }
    }

    public void UpsertPlan(PlanFile plan)
    {
        using (new WriteLockHandle(_lock))
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                UpsertPlanInternal(plan);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void DeletePlan(int planId)
    {
        using (new WriteLockHandle(_lock))
        {
            ExecuteNonQuery("DELETE FROM Plans WHERE Id = @id", new SqliteParameter("@id", planId));
        }
    }

    public void UpdatePlanState(int planId, PlanStatus state)
    {
        using (new WriteLockHandle(_lock))
        {
            ExecuteNonQuery("UPDATE Plans SET State = @state, Updated = @updated WHERE Id = @id",
                new SqliteParameter("@state", state.ToString()),
                new SqliteParameter("@updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                new SqliteParameter("@id", planId));
        }
    }

    public void UpdatePlanContent(int planId, string latestRevisionContent, int revisionCount)
    {
        using (new WriteLockHandle(_lock))
        {
            ExecuteNonQuery("UPDATE Plans SET LatestRevisionContent = @content, RevisionCount = @count, Updated = @updated WHERE Id = @id",
                new SqliteParameter("@content", latestRevisionContent),
                new SqliteParameter("@count", revisionCount),
                new SqliteParameter("@updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                new SqliteParameter("@id", planId));
        }
    }

    public void UpdateRecommendationState(int planId, string recommendationTitle, string newState,
        string? declineReason)
    {
        using (new WriteLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              UPDATE Recommendations SET State = @state, DeclineReason = @reason
                              WHERE PlanId = @planId AND Title = @title
                              """;
            cmd.Parameters.AddWithValue("@state", newState);
            cmd.Parameters.AddWithValue("@reason", (object?)declineReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@planId", planId);
            cmd.Parameters.AddWithValue("@title", recommendationTitle);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpsertCosts(int planId, List<CostEntry> costs)
    {
        using (new WriteLockHandle(_lock))
        {
            using var deleteCmd = _connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Costs WHERE PlanId = @planId";
            deleteCmd.Parameters.AddWithValue("@planId", planId);
            deleteCmd.ExecuteNonQuery();

            if (costs.Count == 0) return;

            using var insertCmd = _connection.CreateCommand();
            insertCmd.CommandText = """
                                    INSERT INTO Costs (PlanId, Promptware, Tokens, Cost, LogTimestamp)
                                    VALUES (@planId, @promptware, @tokens, @cost, @logTimestamp)
                                    """;
            insertCmd.Parameters.AddWithValue("@planId", planId);
            insertCmd.Parameters.AddWithValue("@promptware", string.Empty);
            insertCmd.Parameters.AddWithValue("@tokens", 0);
            insertCmd.Parameters.AddWithValue("@cost", 0.0);
            insertCmd.Parameters.AddWithValue("@logTimestamp", DBNull.Value);

            foreach (var cost in costs)
            {
                insertCmd.Parameters["@planId"].Value = planId;
                insertCmd.Parameters["@promptware"].Value = cost.Promptware;
                insertCmd.Parameters["@tokens"].Value = cost.Tokens;
                insertCmd.Parameters["@cost"].Value = (double)cost.Cost;
                insertCmd.Parameters["@logTimestamp"].Value = cost.LogTimestamp.HasValue
                    ? cost.LogTimestamp.Value.ToString("O", CultureInfo.InvariantCulture)
                    : DBNull.Value;
                insertCmd.ExecuteNonQuery();
            }
        }
    }

    public void UpsertRecommendations(int planId, string folderName, List<RecommendationYaml> recommendations,
        string project, string planTitle, DateTime updated, PlanStatus status)
    {
        using (new WriteLockHandle(_lock))
        {
            using var deleteCmd = _connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Recommendations WHERE PlanId = @planId";
            deleteCmd.Parameters.AddWithValue("@planId", planId);
            deleteCmd.ExecuteNonQuery();

            if (recommendations.Count == 0) return;

            using var insertCmd = _connection.CreateCommand();
            insertCmd.CommandText = """
                                    INSERT INTO Recommendations (PlanId, Title, Description, State, DeclineReason,
                                                                 PlanTitle, PlanFolderName, Project, Date, SourcePlanStatus,
                                                                 Impact, Risk)
                                    VALUES (@planId, @title, @description, @state, @declineReason,
                                            @planTitle, @planFolderName, @project, @date, @sourcePlanStatus,
                                            @impact, @risk)
                                    """;
            insertCmd.Parameters.AddWithValue("@planId", planId);
            insertCmd.Parameters.AddWithValue("@title", string.Empty);
            insertCmd.Parameters.AddWithValue("@description", string.Empty);
            insertCmd.Parameters.AddWithValue("@state", string.Empty);
            insertCmd.Parameters.AddWithValue("@declineReason", DBNull.Value);
            insertCmd.Parameters.AddWithValue("@planTitle", string.Empty);
            insertCmd.Parameters.AddWithValue("@planFolderName", string.Empty);
            insertCmd.Parameters.AddWithValue("@project", string.Empty);
            insertCmd.Parameters.AddWithValue("@date", string.Empty);
            insertCmd.Parameters.AddWithValue("@sourcePlanStatus", string.Empty);
            insertCmd.Parameters.AddWithValue("@impact", DBNull.Value);
            insertCmd.Parameters.AddWithValue("@risk", DBNull.Value);

            foreach (var rec in recommendations)
            {
                insertCmd.Parameters["@planId"].Value = planId;
                insertCmd.Parameters["@title"].Value = rec.Title;
                insertCmd.Parameters["@description"].Value = rec.Description;
                insertCmd.Parameters["@state"].Value = string.IsNullOrWhiteSpace(rec.State) ? "Pending" : rec.State;
                insertCmd.Parameters["@declineReason"].Value = (object?)rec.DeclineReason ?? DBNull.Value;
                insertCmd.Parameters["@planTitle"].Value = planTitle;
                insertCmd.Parameters["@planFolderName"].Value = folderName;
                insertCmd.Parameters["@project"].Value = project;
                insertCmd.Parameters["@date"].Value = updated.ToString("O", CultureInfo.InvariantCulture);
                insertCmd.Parameters["@sourcePlanStatus"].Value = status.ToString();
                insertCmd.Parameters["@impact"].Value = (object?)rec.Impact ?? DBNull.Value;
                insertCmd.Parameters["@risk"].Value = (object?)rec.Risk ?? DBNull.Value;
                insertCmd.ExecuteNonQuery();
            }
        }
    }

    public HashSet<int> GetTerminalPlanIds()
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id FROM Plans WHERE State IN ('Completed', 'Skipped')";
            using var reader = cmd.ExecuteReader();
            var ids = new HashSet<int>();
            while (reader.Read())
                ids.Add(reader.GetInt32(0));
            return ids;
        }
    }

    public void BulkUpsertPlans(List<PlanFile> plans, bool forceOverwrite = false)
    {
        using (new WriteLockHandle(_lock))
        {
            if (plans.Count == 0) return;

            using var transaction = _connection.BeginTransaction();
            try
            {
                foreach (var plan in plans)
                    UpsertPlanInternal(plan, forceOverwrite);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void UpsertJob(JobItem job)
    {
        using (new WriteLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              INSERT OR REPLACE INTO Jobs (Id, Type, PlanFile, Project, Status, Provider, SessionId, StartedAt, CompletedAt, DurationSeconds, Cost, Tokens, StatusMessage, Args)
                              VALUES (@id, @type, @planFile, @project, @status, @provider, @sessionId, @startedAt, @completedAt, @durationSeconds, @cost, @tokens, @statusMessage, @args)
                              """;
            cmd.Parameters.AddWithValue("@id", job.Id);
            cmd.Parameters.AddWithValue("@type", job.Type);
            cmd.Parameters.AddWithValue("@planFile", job.PlanFile);
            cmd.Parameters.AddWithValue("@project", job.Project);
            cmd.Parameters.AddWithValue("@status", job.Status.ToString());
            cmd.Parameters.AddWithValue("@provider", job.Provider);
            cmd.Parameters.AddWithValue("@sessionId", (object?)job.SessionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@startedAt",
                job.StartedAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@completedAt",
                job.CompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@durationSeconds", (object?)job.DurationSeconds ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cost", job.Cost.HasValue ? (double)job.Cost.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@tokens", (object?)job.Tokens ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@statusMessage", (object?)job.StatusMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@args", job.Args.Length > 0 ? JsonSerializer.Serialize(job.Args) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public List<JobItem> GetRecentJobs(int limit = 100)
    {
        using (new ReadLockHandle(_lock))
        {
            return ReadList("SELECT * FROM Jobs ORDER BY CompletedAt DESC LIMIT @limit",
                reader => new JobItem
                {
                    Id = reader.GetString(reader.GetOrdinal("Id")),
                    Type = reader.GetString(reader.GetOrdinal("Type")),
                    PlanFile = reader.GetString(reader.GetOrdinal("PlanFile")),
                    Project = reader.GetString(reader.GetOrdinal("Project")),
                    Status = Enum.Parse<JobStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                    Provider = reader.GetString(reader.GetOrdinal("Provider")),
                    SessionId = reader.IsDBNull(reader.GetOrdinal("SessionId"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("SessionId")),
                    StartedAt = reader.IsDBNull(reader.GetOrdinal("StartedAt"))
                        ? null
                        : DateTime.Parse(reader.GetString(reader.GetOrdinal("StartedAt")), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind),
                    CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt"))
                        ? null
                        : DateTime.Parse(reader.GetString(reader.GetOrdinal("CompletedAt")),
                            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    DurationSeconds = reader.IsDBNull(reader.GetOrdinal("DurationSeconds"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("DurationSeconds")),
                    Cost = reader.IsDBNull(reader.GetOrdinal("Cost"))
                        ? null
                        : (decimal)reader.GetDouble(reader.GetOrdinal("Cost")),
                    Tokens = reader.IsDBNull(reader.GetOrdinal("Tokens"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("Tokens")),
                    StatusMessage = reader.IsDBNull(reader.GetOrdinal("StatusMessage"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("StatusMessage")),
                    Args = reader.IsDBNull(reader.GetOrdinal("Args"))
                        ? []
                        : JsonSerializer.Deserialize<string[]>(reader.GetString(reader.GetOrdinal("Args"))) ?? []
                },
                new SqliteParameter("@limit", limit));
        }
    }

    public void PurgeOldJobs(int keepCount = 500)
    {
        using (new WriteLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              DELETE FROM Jobs
                              WHERE Id NOT IN (
                                  SELECT Id FROM Jobs
                                  ORDER BY CompletedAt DESC
                                  LIMIT @keepCount
                              )
                              """;
            cmd.Parameters.AddWithValue("@keepCount", keepCount);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteJob(string id)
    {
        using (new WriteLockHandle(_lock))
        {
            ExecuteNonQuery("DELETE FROM Jobs WHERE Id = @id", new SqliteParameter("@id", id));
        }
    }

    public long GetDatabaseSize()
    {
        using (new ReadLockHandle(_lock))
        {
            return ExecuteScalar<long>("SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()");
        }
    }

    public DateTime GetLastSyncTime()
    {
        using (new ReadLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM SyncMetadata WHERE Key = 'LastSyncTime'";
            var result = cmd.ExecuteScalar();
            if (result is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return DateTime.MinValue;
        }
    }

    public void SetLastSyncTime(DateTime time)
    {
        using (new WriteLockHandle(_lock))
        {
            ExecuteNonQuery("""
                            INSERT INTO SyncMetadata (Key, Value) VALUES ('LastSyncTime', @value)
                            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                            """,
                new SqliteParameter("@value", time.ToString("O", CultureInfo.InvariantCulture)));
        }
    }

    public Dictionary<string, string> GetAllPrStatuses()
    {
        using (new ReadLockHandle(_lock))
        {
            var list = ReadList("SELECT PrUrl, Status FROM PrStatuses", reader =>
                (Url: reader.GetString(0), Status: reader.GetString(1)));

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (url, status) in list)
                result[url] = status;
            return result;
        }
    }

    public void UpsertPrStatus(string prUrl, string owner, string repo, string status, DateTime lastChecked)
    {
        using (new WriteLockHandle(_lock))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO PrStatuses (PrUrl, Owner, Repo, Status, LastChecked)
                              VALUES (@url, @owner, @repo, @status, @checked)
                              ON CONFLICT(PrUrl) DO UPDATE SET
                                  Status = excluded.Status,
                                  LastChecked = excluded.LastChecked
                              """;
            cmd.Parameters.AddWithValue("@url", prUrl);
            cmd.Parameters.AddWithValue("@owner", owner);
            cmd.Parameters.AddWithValue("@repo", repo);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@checked", lastChecked.ToString("O", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }

    public List<string> GetNonMergedPrUrls()
    {
        using (new ReadLockHandle(_lock))
        {
            return ReadList("SELECT PrUrl FROM PrStatuses WHERE Status != 'Merged'",
                reader => reader.GetString(0));
        }
    }

    public void RebuildFtsIndex()
    {
        using (new WriteLockHandle(_lock))
        {
            ExecuteNonQuery("INSERT INTO PlanSearch(PlanSearch) VALUES('delete-all');");
            ExecuteNonQuery("""
                            INSERT INTO PlanSearch(rowid, Title, LatestRevisionContent, Project, InitialPrompt, SourceUrl)
                            SELECT Id, Title, LatestRevisionContent, Project, InitialPrompt, SourceUrl
                            FROM Plans;
                            """);
        }
    }

    /// <summary>
    /// Executes a scalar query and returns the result as T. Must be called within a lock.
    /// </summary>
    private T ExecuteScalar<T>(string sql, params SqliteParameter[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var param in parameters)
            cmd.Parameters.Add(param);

        var result = cmd.ExecuteScalar();
        if (result == null || result is DBNull)
            return default(T)!;

        return (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Executes a query and maps each row to T using the provided mapper function. Must be called within a lock.
    /// </summary>
    private List<T> ReadList<T>(string sql, Func<SqliteDataReader, T> mapper, params SqliteParameter[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var param in parameters)
            cmd.Parameters.Add(param);

        var result = new List<T>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(mapper(reader));

        return result;
    }

    /// <summary>
    /// Executes a non-query command (INSERT, UPDATE, DELETE). Must be called within a lock.
    /// </summary>
    private int ExecuteNonQuery(string sql, params SqliteParameter[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var param in parameters)
            cmd.Parameters.Add(param);

        return cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _connection?.Dispose();
            _lock?.Dispose();
        }

        _disposed = true;
    }

    ~PlanDatabaseService()
    {
        Dispose(disposing: false);
    }

    private static void ValidateIdentifier(string name)
    {
        if (!AllowedTableColumns.Contains(name))
            throw new ArgumentException($"Invalid SQL identifier: {name}");
    }

    /// <summary>
    ///     Builds a PlanFile for a single plan query. Fetches child tables individually.
    ///     For bulk queries, use BuildPlanFileFromRow with pre-fetched child data instead.
    ///     Must be called within a read or write lock.
    /// </summary>
    private PlanFile? BuildPlanFile(SqliteDataReader reader)
    {
        var ordinals = GetPlanRowOrdinals(reader);
        var row = ReadPlanRow(reader, ordinals);
        return BuildPlanFileFromRow(row.Id, row.Title, row.Project, row.Level, row.State,
            row.FolderPath, row.FolderName, row.YamlRaw, row.RevisionCount, row.LatestContent,
            row.Created, row.Updated, row.InitialPrompt, row.SourceUrl,
            GetListForPlan(row.Id, "Repos", "RepoPath"),
            GetListForPlan(row.Id, "Commits", "CommitHash"),
            GetListForPlan(row.Id, "PullRequests", "PrUrl"),
            GetVerificationsForPlan(row.Id),
            GetListForPlan(row.Id, "RelatedPlans", "RelatedPlanPath"),
            GetListForPlan(row.Id, "DependsOn", "DependsOnPlanPath"));
    }

    private static PlanRowOrdinals GetPlanRowOrdinals(SqliteDataReader reader)
    {
        return new PlanRowOrdinals(
            reader.GetOrdinal("Id"),
            reader.GetOrdinal("Title"),
            reader.GetOrdinal("Project"),
            reader.GetOrdinal("Level"),
            reader.GetOrdinal("State"),
            reader.GetOrdinal("FolderPath"),
            reader.GetOrdinal("FolderName"),
            reader.GetOrdinal("YamlRaw"),
            reader.GetOrdinal("RevisionCount"),
            reader.GetOrdinal("LatestRevisionContent"),
            reader.GetOrdinal("Created"),
            reader.GetOrdinal("Updated"),
            reader.GetOrdinal("InitialPrompt"),
            reader.GetOrdinal("SourceUrl")
        );
    }

    private static (int Id, string Title, string Project, string Level, string State,
        string FolderPath, string FolderName, string YamlRaw, int RevisionCount,
        string LatestContent, string Created, string Updated, string? InitialPrompt, string? SourceUrl)
        ReadPlanRow(SqliteDataReader reader, PlanRowOrdinals o)
    {
        return (
            Id: reader.GetInt32(o.Id),
            Title: reader.GetString(o.Title),
            Project: reader.GetString(o.Project),
            Level: reader.GetString(o.Level),
            State: reader.GetString(o.State),
            FolderPath: reader.GetString(o.FolderPath),
            FolderName: reader.GetString(o.FolderName),
            YamlRaw: reader.GetString(o.YamlRaw),
            RevisionCount: reader.GetInt32(o.RevisionCount),
            LatestContent: reader.GetString(o.LatestContent),
            Created: reader.GetString(o.Created),
            Updated: reader.GetString(o.Updated),
            InitialPrompt: reader.GetStringOrNull(o.InitialPrompt),
            SourceUrl: reader.GetStringOrNull(o.SourceUrl)
        );
    }

    private static PlanFile? BuildPlanFileFromRow(int planId, string title, string project, string level,
        string state, string folderPath, string folderName, string yamlRaw, int revisionCount,
        string latestContent, string createdStr, string updatedStr, string? initialPrompt, string? sourceUrl,
        List<string> repos, List<string> commits, List<string> prs,
        List<PlanVerificationEntry> verifications, List<string> relatedPlans, List<string> dependsOn)
    {
        if (!Enum.TryParse<PlanStatus>(state, true, out var status))
            status = PlanStatus.Draft;

        var created = DateTime.Parse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var updated = DateTime.Parse(updatedStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

        var metadata = new PlanMetadata(planId, project, level, title, status,
            repos, commits, prs, verifications, relatedPlans, dependsOn, created, updated, initialPrompt, sourceUrl);

        return new PlanFile(metadata, latestContent, folderPath, yamlRaw, revisionCount);
    }

    private List<string> GetListForPlan(int planId, string table, string column)
    {
        ValidateIdentifier(table);
        ValidateIdentifier(column);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {table} WHERE PlanId = @planId";
        cmd.Parameters.AddWithValue("@planId", planId);

        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        var columnOrdinal = -1;
        while (reader.Read())
        {
            if (columnOrdinal == -1)
                columnOrdinal = reader.GetOrdinal(column);
            list.Add(reader.GetString(columnOrdinal));
        }

        return list;
    }

    private List<PlanVerificationEntry> GetVerificationsForPlan(int planId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Name, Status FROM Verifications WHERE PlanId = @planId";
        cmd.Parameters.AddWithValue("@planId", planId);

        var list = new List<PlanVerificationEntry>();
        using var reader = cmd.ExecuteReader();
        int nameOrdinal = -1, statusOrdinal = -1;
        while (reader.Read())
        {
            if (nameOrdinal == -1)
            {
                nameOrdinal = reader.GetOrdinal("Name");
                statusOrdinal = reader.GetOrdinal("Status");
            }

            list.Add(new PlanVerificationEntry
            {
                Name = reader.GetString(nameOrdinal),
                Status = reader.GetString(statusOrdinal)
            });
        }

        return list;
    }

    private Dictionary<int, List<string>> BatchGetList(List<int> planIds, string table, string column)
    {
        ValidateIdentifier(table);
        ValidateIdentifier(column);
        var result = new Dictionary<int, List<string>>();
        if (planIds.Count == 0) return result;

        const int batchSize = 500;
        for (int i = 0; i < planIds.Count; i += batchSize)
        {
            var batch = planIds.Skip(i).Take(batchSize).ToList();
            using var cmd = _connection.CreateCommand();

            // Build parameterized IN clause
            var parameters = new List<string>();
            for (int j = 0; j < batch.Count; j++)
            {
                var paramName = $"@p{j}";
                parameters.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, batch[j]);
            }

            var inClause = string.Join(", ", parameters);
            cmd.CommandText = $"SELECT PlanId, {column} FROM {table} WHERE PlanId IN ({inClause})";

            using var reader = cmd.ExecuteReader();
            int planIdOrdinal = -1, columnOrdinal = -1;
            while (reader.Read())
            {
                if (planIdOrdinal == -1)
                {
                    planIdOrdinal = reader.GetOrdinal("PlanId");
                    columnOrdinal = reader.GetOrdinal(column);
                }

                var planId = reader.GetInt32(planIdOrdinal);
                if (!result.TryGetValue(planId, out var list))
                {
                    list = new List<string>();
                    result[planId] = list;
                }

                list.Add(reader.GetString(columnOrdinal));
            }
        }

        return result;
    }

    private Dictionary<int, List<PlanVerificationEntry>> BatchGetVerifications(List<int> planIds)
    {
        var result = new Dictionary<int, List<PlanVerificationEntry>>();
        if (planIds.Count == 0) return result;

        const int batchSize = 500;
        for (int i = 0; i < planIds.Count; i += batchSize)
        {
            var batch = planIds.Skip(i).Take(batchSize).ToList();
            using var cmd = _connection.CreateCommand();

            // Build parameterized IN clause
            var parameters = new List<string>();
            for (int j = 0; j < batch.Count; j++)
            {
                var paramName = $"@p{j}";
                parameters.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, batch[j]);
            }

            var inClause = string.Join(", ", parameters);
            cmd.CommandText = $"SELECT PlanId, Name, Status FROM Verifications WHERE PlanId IN ({inClause})";

            using var reader = cmd.ExecuteReader();
            int planIdOrdinal = -1, nameOrdinal = -1, statusOrdinal = -1;
            while (reader.Read())
            {
                if (planIdOrdinal == -1)
                {
                    planIdOrdinal = reader.GetOrdinal("PlanId");
                    nameOrdinal = reader.GetOrdinal("Name");
                    statusOrdinal = reader.GetOrdinal("Status");
                }

                var planId = reader.GetInt32(planIdOrdinal);
                if (!result.TryGetValue(planId, out var list))
                {
                    list = new List<PlanVerificationEntry>();
                    result[planId] = list;
                }

                list.Add(new PlanVerificationEntry
                {
                    Name = reader.GetString(nameOrdinal),
                    Status = reader.GetString(statusOrdinal)
                });
            }
        }

        return result;
    }

    private List<PlanFile> ExecuteSearchQuery(SqliteCommand cmd)
    {
        var planIds = new List<int>();
        var rawPlans = new List<(int Id, string Title, string Project, string Level, string State,
            string FolderPath, string FolderName, string YamlRaw, int RevisionCount,
            string LatestContent, string Created, string Updated, string? InitialPrompt, string? SourceUrl)>();

        using var reader = cmd.ExecuteReader();
        PlanRowOrdinals? ordinals = null;
        while (reader.Read())
        {
            ordinals ??= GetPlanRowOrdinals(reader);
            var row = ReadPlanRow(reader, ordinals.Value);
            planIds.Add(row.Id);
            rawPlans.Add(row);
        }

        if (rawPlans.Count == 0)
            return [];

        var allRepos = BatchGetList(planIds, "Repos", "RepoPath");
        var allCommits = BatchGetList(planIds, "Commits", "CommitHash");
        var allPrs = BatchGetList(planIds, "PullRequests", "PrUrl");
        var allVerifications = BatchGetVerifications(planIds);
        var allRelatedPlans = BatchGetList(planIds, "RelatedPlans", "RelatedPlanPath");
        var allDependsOn = BatchGetList(planIds, "DependsOn", "DependsOnPlanPath");

        var plans = new List<PlanFile>();
        foreach (var row in rawPlans)
        {
            var plan = BuildPlanFileFromRow(row.Id, row.Title, row.Project, row.Level, row.State,
                row.FolderPath, row.FolderName, row.YamlRaw, row.RevisionCount, row.LatestContent,
                row.Created, row.Updated, row.InitialPrompt, row.SourceUrl,
                allRepos.GetValueOrDefault(row.Id, []),
                allCommits.GetValueOrDefault(row.Id, []),
                allPrs.GetValueOrDefault(row.Id, []),
                allVerifications.GetValueOrDefault(row.Id, []),
                allRelatedPlans.GetValueOrDefault(row.Id, []),
                allDependsOn.GetValueOrDefault(row.Id, []));
            if (plan != null)
                plans.Add(plan);
        }

        return plans;
    }

    private void UpsertPlanInternal(PlanFile plan, bool forceOverwrite = false)
    {
        var updateGuard = forceOverwrite ? "" : "WHERE excluded.Updated >= Plans.Updated";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
                           INSERT INTO Plans (Id, Title, Project, Level, State, FolderPath, FolderName,
                                              YamlRaw, RevisionCount, LatestRevisionContent, Created, Updated, InitialPrompt, SourceUrl)
                           VALUES (@id, @title, @project, @level, @state, @folderPath, @folderName,
                                   @yamlRaw, @revisionCount, @latestContent, @created, @updated, @initialPrompt, @sourceUrl)
                           ON CONFLICT(Id) DO UPDATE SET
                               Title = excluded.Title,
                               Project = excluded.Project,
                               Level = excluded.Level,
                               State = excluded.State,
                               FolderPath = excluded.FolderPath,
                               FolderName = excluded.FolderName,
                               YamlRaw = excluded.YamlRaw,
                               RevisionCount = excluded.RevisionCount,
                               LatestRevisionContent = excluded.LatestRevisionContent,
                               Created = excluded.Created,
                               Updated = excluded.Updated,
                               InitialPrompt = excluded.InitialPrompt,
                               SourceUrl = excluded.SourceUrl
                           {updateGuard}
                           """;

        cmd.Parameters.AddWithValue("@id", plan.Id);
        cmd.Parameters.AddWithValue("@title", plan.Title);
        cmd.Parameters.AddWithValue("@project", plan.Project);
        cmd.Parameters.AddWithValue("@level", plan.Level);
        cmd.Parameters.AddWithValue("@state", plan.Status.ToString());
        cmd.Parameters.AddWithValue("@folderPath", plan.FolderPath);
        cmd.Parameters.AddWithValue("@folderName", plan.FolderName);
        cmd.Parameters.AddWithValue("@yamlRaw", plan.PlanYamlRaw);
        cmd.Parameters.AddWithValue("@revisionCount", plan.RevisionCount);
        cmd.Parameters.AddWithValue("@latestContent", plan.LatestRevisionContent);
        cmd.Parameters.AddWithValue("@created", plan.Created.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@updated", plan.Updated.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@initialPrompt", plan.InitialPrompt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@sourceUrl", plan.SourceUrl ?? (object)DBNull.Value);

        cmd.ExecuteNonQuery();

        // Sync child tables
        SyncChildTable(plan.Id, "Repos", "RepoPath", plan.Repos);
        SyncChildTable(plan.Id, "Commits", "CommitHash", plan.Commits);
        SyncChildTable(plan.Id, "PullRequests", "PrUrl", plan.Prs);
        SyncVerifications(plan.Id, plan.Verifications);
        SyncChildTable(plan.Id, "RelatedPlans", "RelatedPlanPath", plan.RelatedPlans);
        SyncChildTable(plan.Id, "DependsOn", "DependsOnPlanPath", plan.DependsOn);
    }

    private void SyncChildTable(int planId, string table, string column, List<string> values)
    {
        ValidateIdentifier(table);
        ValidateIdentifier(column);
        using var deleteCmd = _connection.CreateCommand();
        deleteCmd.CommandText = $"DELETE FROM {table} WHERE PlanId = @planId";
        deleteCmd.Parameters.AddWithValue("@planId", planId);
        deleteCmd.ExecuteNonQuery();

        if (values.Count == 0) return;

        using var insertCmd = _connection.CreateCommand();
        insertCmd.CommandText = $"INSERT INTO {table} (PlanId, {column}) VALUES (@planId, @value)";
        insertCmd.Parameters.AddWithValue("@planId", planId);
        insertCmd.Parameters.AddWithValue("@value", string.Empty);

        foreach (var value in values)
        {
            if (value == null) continue;
            insertCmd.Parameters["@planId"].Value = planId;
            insertCmd.Parameters["@value"].Value = value;
            insertCmd.ExecuteNonQuery();
        }
    }

    private void SyncVerifications(int planId, List<PlanVerificationEntry> verifications)
    {
        using var deleteCmd = _connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM Verifications WHERE PlanId = @planId";
        deleteCmd.Parameters.AddWithValue("@planId", planId);
        deleteCmd.ExecuteNonQuery();

        if (verifications.Count == 0) return;

        using var insertCmd = _connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Verifications (PlanId, Name, Status) VALUES (@planId, @name, @status)";
        insertCmd.Parameters.AddWithValue("@planId", planId);
        insertCmd.Parameters.AddWithValue("@name", string.Empty);
        insertCmd.Parameters.AddWithValue("@status", string.Empty);

        foreach (var v in verifications)
        {
            insertCmd.Parameters["@planId"].Value = planId;
            insertCmd.Parameters["@name"].Value = v.Name;
            insertCmd.Parameters["@status"].Value = v.Status;
            insertCmd.ExecuteNonQuery();
        }
    }

    private readonly record struct PlanRowOrdinals(
        int Id,
        int Title,
        int Project,
        int Level,
        int State,
        int FolderPath,
        int FolderName,
        int YamlRaw,
        int RevisionCount,
        int LatestContent,
        int Created,
        int Updated,
        int InitialPrompt,
        int SourceUrl);
}
