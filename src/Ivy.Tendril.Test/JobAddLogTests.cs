using Ivy.Tendril.Commands;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Mcp;
using Ivy.Tendril.Mcp.Tools;
using Ivy.Tendril.Services.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

/// <summary>
/// Covers the three surfaces of <c>job add-log</c>: the CLI command, the MCP tool and the REST endpoint.
/// All three append an <c>## Agent Log</c> section to the job's log in <c>&lt;TendrilHome&gt;/Jobs/</c>.
/// </summary>
public class JobAddLogTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-job-addlog-test");

    public void Dispose() => _tempDir.Dispose();

    private string SeedJobLog(string stem, string content = "# Job Log\n")
    {
        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Directory.CreateDirectory(jobsDir);
        var path = Path.Combine(jobsDir, $"{stem}.md");
        File.WriteAllText(path, content);
        return path;
    }

    // --- CLI: JobAddLogCommand.WriteLog ---

    [Fact]
    public void WriteLog_AppendsAgentSectionToTheJobLog()
    {
        var logPath = SeedJobLog("00007-30001-CreatePlan");

        var written = JobAddLogCommand.WriteLog(_tempDir.Path, "00007", "CreatePlan");

        Assert.Equal(logPath, written);
        var content = File.ReadAllText(logPath);
        Assert.Contains("# Job Log", content);
        Assert.Contains("## Agent Log — CreatePlan", content);
        // The machine-readable marker is what the completion write looks for.
        Assert.Contains(JobLogWriter.AgentLogMarker, content);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("00007")]
    [InlineData(" 7 ")]
    public void WriteLog_NormalizesTheJobIdLikeTheRestEndpointDoes(string jobId)
    {
        var logPath = SeedJobLog("00007-30001-CreatePlan");

        var written = JobAddLogCommand.WriteLog(_tempDir.Path, jobId, "CreatePlan");

        Assert.Equal(logPath, written);
    }

    [Fact]
    public void WriteLog_ResolvesTheJobLogWithoutKnowingThePlanId()
    {
        // The job id alone identifies the log; the plan id is just a segment of the stem.
        var logPath = SeedJobLog("00011-30002-ExecutePlan");
        SeedJobLog("00010-30002-ExpandPlan");

        var written = JobAddLogCommand.WriteLog(_tempDir.Path, "00011", "ExecutePlan");

        Assert.Equal(logPath, written);
    }

    [Fact]
    public void WriteLog_FindsThePlanlessJobLog()
    {
        var logPath = SeedJobLog("00459-CreatePlan");

        var written = JobAddLogCommand.WriteLog(_tempDir.Path, "00459", "CreatePlan");

        Assert.Equal(logPath, written);
    }

    [Fact]
    public void WriteLog_IgnoresThePromptFileWhenResolvingTheLog()
    {
        var logPath = SeedJobLog("00012-30003-ExecutePlan");
        File.WriteAllText(
            Path.Combine(_tempDir.Path, "Jobs", "00012-30003-ExecutePlan.prompt.md"), "the prompt");

        var written = JobAddLogCommand.WriteLog(_tempDir.Path, "00012", "ExecutePlan");

        Assert.Equal(logPath, written);
    }

    [Fact]
    public void WriteLog_IncludesSummary()
    {
        var logPath = SeedJobLog("00009-30003-ExecutePlan");

        JobAddLogCommand.WriteLog(_tempDir.Path, "00009", "ExecutePlan", "Completed all verifications successfully");

        Assert.Contains("Completed all verifications successfully", File.ReadAllText(logPath));
    }

    [Fact]
    public void WriteLog_AppendsRatherThanOverwrites()
    {
        var logPath = SeedJobLog("00030-30006-ExecutePlan");

        JobAddLogCommand.WriteLog(_tempDir.Path, "00030", "ExecutePlan", "first");
        JobAddLogCommand.WriteLog(_tempDir.Path, "00030", "ExecutePlan", "second");

        var content = File.ReadAllText(logPath);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
    }

    [Fact]
    public void WriteLog_WithNoJobLog_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            JobAddLogCommand.WriteLog(_tempDir.Path, "00004", "ExecutePlan"));
    }

    [Theory]
    [InlineData("{{TendrilJobId}}")]  // firmware placeholder that was never substituted
    [InlineData("../../etc/passwd")]  // path traversal
    [InlineData("00007/../x")]
    public void WriteLog_WithInvalidJobId_ThrowsAndWritesNothing(string jobId)
    {
        Assert.Throws<ArgumentException>(() =>
            JobAddLogCommand.WriteLog(_tempDir.Path, jobId, "ExecutePlan", "junk"));

        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Assert.True(!Directory.Exists(jobsDir) || Directory.GetFiles(jobsDir).Length == 0,
            "an invalid job-id must not write a job log named after it");
    }

    // --- MCP: JobTools.AddLog ---

    private JobTools CreateTools(McpAuthenticationService? authService = null) => new(
        authService ?? new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance),
        new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path));

    [Fact]
    public void McpAddLog_AppendsToTheJobLog()
    {
        var originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        try
        {
            var logPath = SeedJobLog("00042-00001-ExecutePlan");

            var result = CreateTools().AddLog("00042", "ExecutePlan", "Test summary");

            Assert.Contains("Log written", result);
            var content = File.ReadAllText(logPath);
            Assert.Contains("## Agent Log — ExecutePlan", content);
            Assert.Contains("Test summary", content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", originalToken);
        }
    }

    [Fact]
    public void McpAddLog_RequiresAuthentication()
    {
        var originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");

        // The service snapshots the expected token at construction; clearing it afterwards
        // simulates a caller that cannot present it.
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "secret-token");
        var authedService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        try
        {
            SeedJobLog("00042-00001-ExecutePlan");

            var result = CreateTools(authedService).AddLog("00042", "ExecutePlan");

            Assert.Contains("Error: Authentication failed", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", originalToken);
        }
    }

    // --- REST: POST api/jobs/{jobId}/logs ---

    // AddLog is pure filesystem work — it never reaches IJobService.
    private JobController CreateController() =>
        new(jobService: null!, new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public void RestAddLog_AppendsToTheJobLog()
    {
        var logPath = SeedJobLog("00042-00001-ExecutePlan");

        var result = CreateController().AddLog("00042", new AddLogRequest("ExecutePlan", "Test summary"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("## Agent Log — ExecutePlan", File.ReadAllText(logPath));
    }

    [Fact]
    public void RestAddLog_NormalizesABareJobNumber()
    {
        var logPath = SeedJobLog("00042-00001-ExecutePlan");

        var result = CreateController().AddLog("42", new AddLogRequest("ExecutePlan"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("## Agent Log — ExecutePlan", File.ReadAllText(logPath));
    }

    [Fact]
    public void RestAddLog_WithNoJobLog_ReturnsNotFound()
    {
        var result = CreateController().AddLog("00042", new AddLogRequest("ExecutePlan"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void RestAddLog_WithInvalidJobId_ReturnsBadRequest()
    {
        var result = CreateController().AddLog("{{TendrilJobId}}", new AddLogRequest("ExecutePlan"));

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
