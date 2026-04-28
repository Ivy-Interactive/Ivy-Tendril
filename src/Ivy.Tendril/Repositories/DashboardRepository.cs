using System.Globalization;
using Ivy.Tendril.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Repositories;

public class DashboardRepository
{
    private readonly SqliteConnection _connection;
    private readonly ILogger _logger;
    private readonly ReaderWriterLockSlim _lock;

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

    public DashboardRepository(SqliteConnection connection, ReaderWriterLockSlim lockSlim, ILogger logger)
    {
        _connection = connection;
        _lock = lockSlim;
        _logger = logger;
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
}
