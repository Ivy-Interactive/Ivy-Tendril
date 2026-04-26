using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Test;

public class ApiKeyAuthMiddlewareTests
{
    [Fact]
    public async Task ApiRoute_WithValidKey_CallsNext()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "secret-123" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/plans/00001";
        context.Request.Headers["X-Api-Key"] = "secret-123";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_WithInvalidKey_Returns401()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "secret-123" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/plans/00001";
        context.Request.Headers["X-Api-Key"] = "wrong-key";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_WithMissingKey_Returns401()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "secret-123" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/plans/00001";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_NoAuthConfigured_CallsNext()
    {
        var settings = new TendrilSettings();
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/plans/00001";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ApiRoute_EmptyApiKey_AllowsAccess()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/inbox";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NonApiRoute_WithAuthConfigured_SkipsAuth()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "secret-123" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/ivy/health";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ApiRoute_JobsEndpoint_SkipsAuth()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "secret-123" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/jobs/job-1/status";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ApiRoute_ProtectsInboxEndpoint()
    {
        var settings = new TendrilSettings { Api = new ApiSettings { ApiKey = "secret-123" } };
        var configService = new ConfigService(settings, "/tmp");
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configService);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/inbox";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }
}
