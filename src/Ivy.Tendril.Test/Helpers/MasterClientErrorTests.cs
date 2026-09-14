using System.Text.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Helpers;

public class MasterClientErrorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string WriteClaim(int pid, int port, TimeSpan? heartbeatAge = null)
    {
        var home = Path.Combine(Path.GetTempPath(), $"tendril-discover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);

        var stamp = DateTime.UtcNow - (heartbeatAge ?? TimeSpan.Zero);
        File.WriteAllText(Path.Combine(home, ".master"), JsonSerializer.Serialize(
            new MasterElectionService.MasterFileData
            {
                Pid = pid,
                Port = port,
                Scheme = "http",
                StartedAt = stamp,
                Heartbeat = stamp
            }, JsonOptions));

        return home;
    }

    [Fact]
    public void Discover_FindsThisProcessOwnServer()
    {
        // Self-exclusion would be wrong here: an embedded server talking to its own API is legitimate,
        // and this is what the CLI running inside the server process does.
        var home = WriteClaim(Environment.ProcessId, 5010);

        try
        {
            var discovery = MasterClient.Discover(home);
            Assert.Equal("http://localhost:5010", discovery.BaseUrl);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void Discover_SaysStartingUp_WhenTheClaimHasNoPortYet()
    {
        var home = WriteClaim(Environment.ProcessId, 0);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MasterClient.Discover(home));
            Assert.Contains("still starting up", ex.Message);

            // Retrying is the right answer, so the claim must survive.
            Assert.True(File.Exists(Path.Combine(home, ".master")));
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void Discover_CleansUpAndReportsAStaleClaim()
    {
        var home = WriteClaim(Environment.ProcessId, 5010,
            heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromMinutes(1));

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MasterClient.Discover(home));
            Assert.Contains("heartbeat stale", ex.Message);
            Assert.False(File.Exists(Path.Combine(home, ".master")));
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void Discover_ReportsNoServer_WhenThereIsNoClaim()
    {
        var home = Path.Combine(Path.GetTempPath(), $"tendril-discover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MasterClient.Discover(home));
            Assert.Contains("No Tendril server is running", ex.Message);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void DescribeFailure_NotFound_NamesEndpointAndMentionsRestartOrDeletion()
    {
        var message = MasterClient.DescribeFailure(404, "api/jobs/00432/status", "");

        Assert.Contains("api/jobs/00432/status", message);
        Assert.Contains("restarted", message);
        Assert.Contains("deleted", message);
    }

    [Fact]
    public void DescribeFailure_Unauthorized_ReturnsAuthenticationFailedMessage()
    {
        var message = MasterClient.DescribeFailure(401, "api/jobs", "");

        Assert.Equal("Authentication failed. Check Api.ApiKey in config.yaml.", message);
    }

    [Fact]
    public void DescribeFailure_JsonErrorBody_SurfacesErrorProperty()
    {
        var message = MasterClient.DescribeFailure(400, "api/jobs", "{\"error\":\"Job not found\"}");

        Assert.Equal("Job not found", message);
    }

    [Fact]
    public void DescribeFailure_NonJsonBody_FallsBackToRawStatusAndBody()
    {
        var message = MasterClient.DescribeFailure(500, "api/jobs/00001/status", "Internal Server Error");

        Assert.Equal("Server returned 500 for api/jobs/00001/status: Internal Server Error", message);
    }
}
