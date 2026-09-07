using System.Globalization;
using Ivy.Tendril.Models;
using Microsoft.Data.Sqlite;

namespace Ivy.Tendril.Database;

public class DashboardRepository(SqliteConnection connection, ReaderWriterLockSlim lockSlim)
{
    /// <summary>
    ///     How far back the daily series go: 365 days the trend chart plots, six leading days so its
    ///     first plotted point has a full 7 day rolling window, and 365 more for the prior-year
    ///     comparison the long range draws against.
    /// </summary>
    internal const int DailyTrendWindowDays = 736;

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

    public DashboardModels GetDashboardData(string? projectFilter)
    {
        using (new ReadLockHandle(lockSlim))
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-6).ToString("yyyy-MM-dd");
            var pf = projectFilter != null ? " AND Project = @project" : "";
            var pfAlias = projectFilter != null ? " AND p.Project = @project" : "";
            var pfAlias2 = projectFilter != null ? " AND p2.Project = @project2" : "";

            // Query 1: Status counts + avg cost (LAST 7 DAYS - filtered by cutoff)
            int totalCount, draftCount, inProgressCount, reviewCount, completedCount, failedCount;
            decimal avgCost;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT
                        COUNT(*) AS TotalCount,
                        COALESCE(SUM(CASE WHEN State IN ('Draft', 'Blocked') THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State IN ('Creating', 'Executing', 'Updating') THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State = 'Review' THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State = 'Completed' THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN State = 'Failed' THEN 1 ELSE 0 END), 0),
                        -- Averaged over the plans that could be priced, not over every plan with a
                        -- Costs row: a subscription run stores NULL, and counting it in the divisor
                        -- would report an average nobody spent. COUNT(column) skips NULL for us.
                        (SELECT CASE WHEN COUNT(DISTINCT CASE WHEN c2.Cost IS NOT NULL THEN p2.Id END) > 0
                            THEN COALESCE(SUM(c2.Cost), 0)
                                 / COUNT(DISTINCT CASE WHEN c2.Cost IS NOT NULL THEN p2.Id END) ELSE 0 END
                         FROM Costs c2 JOIN Plans p2 ON p2.Id = c2.PlanId
                         WHERE p2.Created >= @cutoff AND p2.State IN ('Completed', 'Failed', 'Review') {pfAlias2}
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

            using (var cmd = connection.CreateCommand())
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
                        SELECT DATE(p.Updated) AS d, COALESCE(SUM(c.Cost), 0) AS cost, SUM(c.Tokens) AS tokens
                        FROM Costs c JOIN Plans p ON p.Id = c.PlanId
                        WHERE p.Updated >= @cutoff AND p.State IN ('Completed', 'Failed', 'Review') {pfAlias}
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
            using (var cmd = connection.CreateCommand())
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

    public DashboardActivityStats GetActivityStats(int monthsBack = 24)
    {
        using (new ReadLockHandle(lockSlim))
        {
            var today = DateTime.UtcNow.Date;
            var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(monthsBack - 1));
            var cutoff = firstMonth.ToString("yyyy-MM-dd");

            var created = new Dictionary<string, int>();
            var prs = new Dictionary<string, int>();
            var costs = new Dictionary<string, decimal>();
            var tokens = new Dictionary<string, long>();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT strftime('%Y-%m', Created) AS ym, COUNT(*)
                    FROM Plans WHERE Created >= @cutoff GROUP BY ym
                    """;
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    created[r.GetString(0)] = r.GetInt32(1);
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT strftime('%Y-%m', p.Updated) AS ym, COUNT(*)
                    FROM PullRequests pr JOIN Plans p ON p.Id = pr.PlanId
                    WHERE p.Updated >= @cutoff AND p.State = 'Completed'
                    GROUP BY ym
                    """;
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    prs[r.GetString(0)] = r.GetInt32(1);
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT strftime('%Y-%m', p.Updated) AS ym, COALESCE(SUM(c.Cost), 0), SUM(c.Tokens)
                    FROM Costs c JOIN Plans p ON p.Id = c.PlanId
                    WHERE p.Updated >= @cutoff AND p.State IN ('Completed', 'Failed', 'Review')
                    GROUP BY ym
                    """;
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var key = r.GetString(0);
                    costs[key] = Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture);
                    tokens[key] = Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture);
                }
            }

            decimal prevWeekAvgCost;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT CASE WHEN COUNT(DISTINCT CASE WHEN c.Cost IS NOT NULL THEN p.Id END) > 0
                        THEN COALESCE(SUM(c.Cost), 0)
                             / COUNT(DISTINCT CASE WHEN c.Cost IS NOT NULL THEN p.Id END) ELSE 0 END
                    FROM Costs c JOIN Plans p ON p.Id = c.PlanId
                    WHERE p.Created >= @from AND p.Created < @to
                      AND p.State IN ('Completed', 'Failed', 'Review')
                    """;
                cmd.Parameters.AddWithValue("@from", today.AddDays(-13).ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", today.AddDays(-6).ToString("yyyy-MM-dd"));
                prevWeekAvgCost = Convert.ToDecimal(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            // Deliberately unfiltered by p.State: money an Executing plan has spent is already spent,
            // and dropping it is a large part of why the monthly figures above read low. Bucketed on
            // the cost row's own timestamp where it has one, so spend lands on the day it happened
            // rather than the day the plan was last touched.
            var dailyCosts = new List<DashboardDailyCost>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DATE(COALESCE(c.LogTimestamp, p.Updated)) AS d,
                           COALESCE(SUM(c.Cost), 0), COALESCE(SUM(c.Tokens), 0)
                    FROM Costs c JOIN Plans p ON p.Id = c.PlanId
                    WHERE COALESCE(c.LogTimestamp, p.Updated) >= @cutoff
                    GROUP BY d ORDER BY d
                    """;
                cmd.Parameters.AddWithValue("@cutoff",
                    today.AddDays(-(DailyTrendWindowDays - 1)).ToString("O", CultureInfo.InvariantCulture));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!DateOnly.TryParse(r.GetString(0), CultureInfo.InvariantCulture, out var day))
                        continue;
                    dailyCosts.Add(new DashboardDailyCost(
                        day,
                        Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture),
                        Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture)));
                }
            }

            var dailyPlans = new Dictionary<DateOnly, int>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DATE(Created) AS d, COUNT(*)
                    FROM Plans
                    WHERE Created >= @cutoff
                    GROUP BY d
                    """;
                cmd.Parameters.AddWithValue("@cutoff",
                    today.AddDays(-(DailyTrendWindowDays - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (DateOnly.TryParse(r.GetString(0), CultureInfo.InvariantCulture, out var day))
                        dailyPlans[day] = r.GetInt32(1);
                }
            }

            // Where the daily series stop being silent about a gap and start meaning it. Clamped up to
            // the retrieval window, because a record older than the window is not in the series either.
            var windowStart = DateOnly.FromDateTime(today.AddDays(-(DailyTrendWindowDays - 1)));
            DateOnly? dailyDataStart = null;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT MIN(d) FROM (
                        SELECT DATE(Created) AS d FROM Plans
                        UNION ALL
                        SELECT DATE(COALESCE(c.LogTimestamp, p.Updated)) AS d
                        FROM Costs c JOIN Plans p ON p.Id = c.PlanId
                    )
                    """;
                if (cmd.ExecuteScalar() is string earliestText
                    && DateOnly.TryParse(earliestText, CultureInfo.InvariantCulture, out var earliest))
                    dailyDataStart = earliest > windowStart ? earliest : windowStart;
            }

            var months = new List<DashboardMonthStats>(monthsBack);
            for (var i = 0; i < monthsBack; i++)
            {
                var month = firstMonth.AddMonths(i);
                var key = month.ToString("yyyy-MM");
                months.Add(new DashboardMonthStats(
                    month.Year,
                    month.Month,
                    created.GetValueOrDefault(key),
                    prs.GetValueOrDefault(key),
                    costs.GetValueOrDefault(key),
                    tokens.GetValueOrDefault(key)
                ));
            }

            return new DashboardActivityStats(
                months, prevWeekAvgCost, dailyCosts, dailyPlans, dailyDataStart);
        }
    }

    public List<RecentMergedPrDto> GetRecentMergedPrs(int limit = 50)
    {
        using (new ReadLockHandle(lockSlim))
        {
            var results = new List<RecentMergedPrDto>();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT pr.PrUrl, p.Id, p.Title,
                       (SELECT r.RepoPath FROM Repos r WHERE r.PlanId = p.Id LIMIT 1) AS Repo,
                       p.Updated
                FROM PullRequests pr
                JOIN Plans p ON p.Id = pr.PlanId
                WHERE p.State = 'Completed'
                ORDER BY p.Updated DESC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@limit", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var prUrl = r.GetString(0);
                var planId = r.GetInt32(1);
                var title = r.GetString(2);
                var repo = r.IsDBNull(3) ? null : r.GetString(3);
                var updatedStr = r.GetString(4);
                var updated = DateTime.TryParse(updatedStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)
                    ? dt
                    : DateTime.UtcNow;
                results.Add(new RecentMergedPrDto(prUrl, planId, title, repo, updated));
            }
            return results;
        }
    }

    public List<RecentPlanCostDto> GetRecentPlanCosts(int days = 7)
    {
        using (new ReadLockHandle(lockSlim))
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
            var results = new List<RecentPlanCostDto>();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT p.Id, p.Title, p.State, p.Created,
                       SUM(c.Cost) AS TotalCost,
                       COUNT(CASE WHEN c.Cost IS NOT NULL THEN 1 END) AS PricedRows,
                       COALESCE(SUM(c.Tokens), 0) AS TotalTokens
                FROM Plans p
                LEFT JOIN Costs c ON c.PlanId = p.Id
                WHERE p.Created >= @cutoff AND p.State IN ('Completed', 'Failed', 'Review')
                GROUP BY p.Id, p.Title, p.State, p.Created
                ORDER BY p.Created DESC
                """;
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var planId = r.GetInt32(0);
                var title = r.GetString(1);
                var state = r.GetString(2);
                var createdStr = r.GetString(3);
                var created = DateTime.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)
                    ? dt
                    : DateTime.UtcNow;
                var pricedRows = r.GetInt32(5);
                decimal? cost = pricedRows > 0 && !r.IsDBNull(4)
                    ? Convert.ToDecimal(r.GetValue(4), CultureInfo.InvariantCulture)
                    : null;
                var tokens = Convert.ToInt64(r.GetValue(6), CultureInfo.InvariantCulture);
                results.Add(new RecentPlanCostDto(planId, title, state, created, cost, tokens));
            }
            return results;
        }
    }
}
