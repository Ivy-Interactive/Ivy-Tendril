using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Connections;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

public class ConnectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PlanDatabaseService _db;

    public ConnectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tendril_test_{Guid.NewGuid():N}.db");
        _db = new PlanDatabaseService(_dbPath, NullLogger<PlanDatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void Should_Perform_CRUD_On_Connections()
    {
        // 1. Add connection
        var connection = new ConnectionItem
        {
            Name = "test-slack",
            Provider = "Slack",
            ConnectionString = "{\"Token\":\"test-token-value\"}",
            Permissions = "send-message,add-reaction",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };

        _db.UpsertConnection(connection);

        // 2. Read connection
        var retrieved = _db.GetConnectionByName("test-slack");
        Assert.NotNull(retrieved);
        Assert.Equal("test-slack", retrieved.Name);
        Assert.Equal("Slack", retrieved.Provider);
        Assert.Equal("{\"Token\":\"test-token-value\"}", retrieved.ConnectionString);
        Assert.Equal("send-message,add-reaction", retrieved.Permissions);

        // 3. List connections
        var list = _db.GetConnections();
        Assert.Single(list);
        Assert.Equal("test-slack", list[0].Name);

        // 4. Update connection
        var updatedConnection = connection with
        {
            Permissions = "*",
            ConnectionString = "{\"Token\":\"updated-token\"}"
        };
        _db.UpsertConnection(updatedConnection);

        var retrievedUpdated = _db.GetConnectionByName("test-slack");
        Assert.NotNull(retrievedUpdated);
        Assert.Equal("*", retrievedUpdated.Permissions);
        Assert.Equal("{\"Token\":\"updated-token\"}", retrievedUpdated.ConnectionString);

        // 5. Delete connection
        _db.DeleteConnection("test-slack");
        Assert.Null(_db.GetConnectionByName("test-slack"));
        Assert.Empty(_db.GetConnections());
    }

    [Fact]
    public async Task Should_Test_Mock_Slack_Connection_Successfully()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            Assert.Equal("https://slack.com/api/auth.test", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("xoxb-valid", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            };
        });

        var clientFactory = new MockHttpClientFactory(handler);
        var executor = new ConnectionExecutorService(clientFactory, new IConnectionProvider[] { new SlackConnection() });

        var conn = new ConnectionItem
        {
            Name = "my-slack",
            Provider = "Slack",
            ConnectionString = "{\"Token\":\"xoxb-valid\"}"
        };

        var (success, error) = await executor.TestConnectionAsync(conn);
        Assert.True(success);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Should_Fail_Slack_Test_On_Invalid_Credentials()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":false,\"error\":\"invalid_auth\"}")
            };
        });

        var clientFactory = new MockHttpClientFactory(handler);
        var executor = new ConnectionExecutorService(clientFactory, new IConnectionProvider[] { new SlackConnection() });

        var conn = new ConnectionItem
        {
            Name = "my-slack",
            Provider = "Slack",
            ConnectionString = "{\"Token\":\"xoxb-invalid\"}"
        };

        var (success, error) = await executor.TestConnectionAsync(conn);
        Assert.False(success);
        Assert.Contains("invalid_auth", error);
    }

    [Fact]
    public async Task Should_Execute_Slack_Send_Message()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            Assert.Equal("https://slack.com/api/chat.postMessage", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"channel\":\"C12345\",\"ts\":\"123.456\"}")
            };
        });

        var clientFactory = new MockHttpClientFactory(handler);
        var executor = new ConnectionExecutorService(clientFactory, new IConnectionProvider[] { new SlackConnection() });

        var conn = new ConnectionItem
        {
            Name = "my-slack",
            Provider = "Slack",
            ConnectionString = "{\"Token\":\"xoxb-token\"}"
        };

        var (success, result) = await executor.ExecuteActionAsync(conn, "send-message", "{\"channel\":\"general\",\"text\":\"hello\"}");
        Assert.True(success);
        Assert.Contains("C12345", result);
    }

    private class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFunc = responseFunc;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFunc(request));
        }
    }

    private class MockHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = handler;

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler);
        }
    }
}
