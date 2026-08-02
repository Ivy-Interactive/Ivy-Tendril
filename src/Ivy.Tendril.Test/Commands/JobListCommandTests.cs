using Microsoft.Data.Sqlite;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Database;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class JobListCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-job-list-test");
    private readonly string _originalTendrilHome;
    private readonly string _dbPath;

    public JobListCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
        _dbPath = Path.Combine(_tempDir.Path, "tendril.db");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate");
        conn.Open();
        var migrator = new DatabaseMigrator(conn);
        migrator.ApplyMigrations();
    }

    private void InsertJob(string id, string type, string project, string status, string? planId = null,
        DateTime? startedAt = null, DateTime? completedAt = null, int? durationSeconds = null, bool cleared = false)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Jobs (Id, Type, Project, Status, ReportedPlanId, StartedAt, CompletedAt, DurationSeconds, Cost, Cleared)
            VALUES (@id, @type, @project, @status, @planId, @startedAt, @completedAt, @durationSeconds, 0.0, @cleared)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@planId", (object?)planId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", (object?)startedAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@completedAt", (object?)completedAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@durationSeconds", (object?)durationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cleared", cleared ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void JobList_ReturnsJobsForProject()
    {
        InitializeDatabase();
        InsertJob("job-001", "ExecutePlan", "ProjectA", "Completed", "00123",
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 300);
        InsertJob("job-002", "CreatePlan", "ProjectB", "Running", "00124",
            DateTime.UtcNow.AddMinutes(-10));
        InsertJob("job-003", "ExecutePlan", "ProjectA", "Failed", "00125",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-30), 5400);

        var settings = new JobListSettings { Project = "ProjectA" };
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal("ProjectA", j.Project));
    }

    [Fact]
    public void JobList_FiltersByStatus()
    {
        InitializeDatabase();
        InsertJob("job-001", "ExecutePlan", "ProjectA", "Completed", startedAt: DateTime.UtcNow.AddHours(-1),
            completedAt: DateTime.UtcNow);
        InsertJob("job-002", "CreatePlan", "ProjectA", "Running", startedAt: DateTime.UtcNow);
        InsertJob("job-003", "ExecutePlan", "ProjectA", "Failed", startedAt: DateTime.UtcNow.AddHours(-2),
            completedAt: DateTime.UtcNow.AddMinutes(-30));

        var settings = new JobListSettings { Status = "Running" };
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Single(jobs);
        Assert.Equal("Running", jobs[0].Status);
    }

    [Fact]
    public void JobList_FiltersByType()
    {
        InitializeDatabase();
        InsertJob("job-001", "ExecutePlan", "ProjectA", "Completed", startedAt: DateTime.UtcNow);
        InsertJob("job-002", "CreatePlan", "ProjectA", "Completed", startedAt: DateTime.UtcNow);
        InsertJob("job-003", "ExecutePlan", "ProjectA", "Completed", startedAt: DateTime.UtcNow);

        var settings = new JobListSettings { Type = "CreatePlan" };
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Single(jobs);
        Assert.Equal("CreatePlan", jobs[0].Type);
    }

    [Fact]
    public void JobList_OrdersInFlightJobsFirst()
    {
        InitializeDatabase();
        InsertJob("job-001", "ExecutePlan", "ProjectA", "Completed",
            startedAt: DateTime.UtcNow.AddHours(-5), completedAt: DateTime.UtcNow.AddHours(-4));
        InsertJob("job-002", "ExecutePlan", "ProjectA", "Running",
            startedAt: DateTime.UtcNow.AddHours(-10));
        InsertJob("job-003", "ExecutePlan", "ProjectA", "Completed",
            startedAt: DateTime.UtcNow.AddHours(-1), completedAt: DateTime.UtcNow);

        var settings = new JobListSettings();
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Equal("job-002", jobs[0].Id);
    }

    [Fact]
    public void JobList_ExcludesClearedJobs()
    {
        InitializeDatabase();
        InsertJob("job-001", "ExecutePlan", "ProjectA", "Completed", cleared: false, startedAt: DateTime.UtcNow);
        InsertJob("job-002", "ExecutePlan", "ProjectA", "Completed", cleared: true, startedAt: DateTime.UtcNow);

        var settings = new JobListSettings();
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Single(jobs);
        Assert.Equal("job-001", jobs[0].Id);
    }

    [Fact]
    public void JobList_RespectsLimit()
    {
        InitializeDatabase();
        for (int i = 1; i <= 100; i++)
        {
            InsertJob($"job-{i:D3}", "ExecutePlan", "ProjectA", "Completed",
                startedAt: DateTime.UtcNow.AddHours(-i));
        }

        var settings = new JobListSettings { Limit = 10 };
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Equal(10, jobs.Count);
    }

    [Fact]
    public void JobList_EmptyDatabase_ReturnsZeroCount()
    {
        InitializeDatabase();

        var settings = new JobListSettings();
        var jobs = JobListCommand.QueryJobs(_dbPath, settings);

        Assert.Empty(jobs);
    }

    [Fact]
    public void JobList_MissingDatabase_ReturnsZeroCount()
    {
        var missingDbPath = Path.Combine(_tempDir.Path, "nonexistent.db");

        var settings = new JobListSettings();
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => JobListCommand.QueryJobs(missingDbPath, settings));
    }

    [Fact]
    public void JobList_InvalidStatus_FailsValidation()
    {
        var settings = new JobListSettings { Status = "InvalidStatus" };
        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Invalid value", result.Message);
    }

    [Fact]
    public void JobList_InvalidLimit_FailsValidation()
    {
        var settings = new JobListSettings { Limit = -1 };
        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("must be a positive integer", result.Message);
    }
}
