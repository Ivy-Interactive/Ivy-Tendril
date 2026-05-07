using System.Net;
using System.Text.Json;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class BitbucketServiceTests
{
    [Fact]
    public async Task GetPrStatusesAsync_ParsesIdAndReturnsStatus()
    {
        var responseJson = JsonSerializer.Serialize(new { state = "MERGED" });
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson)
        });

        var factory = new FakeHttpClientFactory(handler);
        var service = new BitbucketService(factory, NullLogger<BitbucketService>.Instance);

        var urls = new List<string> { "https://bitbucket.org/workspace/repo/pull-requests/123" };
        var (statuses, error) = await service.GetPrStatusesAsync("workspace", "repo", urls);

        Assert.Null(error);
        Assert.Single(statuses);
        Assert.Equal("Merged", statuses["https://bitbucket.org/workspace/repo/pull-requests/123"]);
        Assert.Equal("/2.0/repositories/workspace/repo/pullrequests/123", handler.LastRequest?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetPrStatusesAsync_ReturnsRawStateForUnknownState()
    {
        var responseJson = JsonSerializer.Serialize(new { state = "UNKNOWN_STATE" });
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson)
        });

        var factory = new FakeHttpClientFactory(handler);
        var service = new BitbucketService(factory, NullLogger<BitbucketService>.Instance);

        var urls = new List<string> { "https://bitbucket.org/workspace/repo/pull-requests/456" };
        var (statuses, error) = await service.GetPrStatusesAsync("workspace", "repo", urls);

        Assert.Null(error);
        Assert.Single(statuses);
        Assert.Equal("UNKNOWN_STATE", statuses["https://bitbucket.org/workspace/repo/pull-requests/456"]);
    }

    [Fact]
    public async Task GetPrStatusesAsync_ReturnsUnauthorizedError()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized
        });

        var factory = new FakeHttpClientFactory(handler);
        var service = new BitbucketService(factory, NullLogger<BitbucketService>.Instance);

        var urls = new List<string> { "https://bitbucket.org/workspace/repo/pull-requests/789" };
        var (statuses, error) = await service.GetPrStatusesAsync("workspace", "repo", urls);

        Assert.NotNull(error);
        Assert.Contains("Unauthorized", error);
        Assert.Empty(statuses);
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler);
        }
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
