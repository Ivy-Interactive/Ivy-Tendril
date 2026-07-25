using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class MasterClientErrorTests
{
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
