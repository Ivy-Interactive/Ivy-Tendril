using Ivy.Tendril.Services.Share;
using Ivy.Tendril.Services.Tunnel;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Test.Services;

public class ShareContextTests
{
    private class DummyShareTunnelService : IShareTunnelService
    {
        public int SharePort => 5011;
        public TunnelStatus Status => TunnelStatus.Connected;
        public string? TunnelUrl => "https://test-share.trycloudflare.com";
        public string? ErrorMessage => null;
        public bool IsConnected => true;
        public bool IsInstalled => true;
        public event Action<TunnelStatus>? StatusChanged { add { } remove { } }
        public Task<bool> CheckInstalledAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task InstallAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeactivateAsync() => Task.CompletedTask;
        public string GetShareUrlForPlan(string planFolderName, bool isReview = true) => $"https://test-share.trycloudflare.com/review?planId={planFolderName}";
    }

    private readonly IShareTunnelService _shareTunnelService;

    public ShareContextTests()
    {
        _shareTunnelService = new DummyShareTunnelService();
    }

    [Fact]
    public void IsShareMode_WhenNoHttpContext_ReturnsFalse()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var shareContext = new ShareContext(accessor, _shareTunnelService);

        Assert.False(shareContext.IsShareMode);
        Assert.NotNull(shareContext.Persona);
    }

    [Fact]
    public void IsShareMode_WithQueryParamShare1_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?share=1");

        var accessor = new HttpContextAccessor { HttpContext = context };
        var shareContext = new ShareContext(accessor, _shareTunnelService);

        Assert.True(shareContext.IsShareMode);
    }

    [Fact]
    public void IsShareMode_WithHeaderXTendrilShare_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tendril-Share"] = "true";

        var accessor = new HttpContextAccessor { HttpContext = context };
        var shareContext = new ShareContext(accessor, _shareTunnelService);

        Assert.True(shareContext.IsShareMode);
    }

    [Fact]
    public void IsShareMode_WithItemIsShareMode_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Items["IsShareMode"] = true;

        var accessor = new HttpContextAccessor { HttpContext = context };
        var shareContext = new ShareContext(accessor, _shareTunnelService);

        Assert.True(shareContext.IsShareMode);
    }

    [Fact]
    public void SetPersona_UpdatesCurrentPersona()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var shareContext = new ShareContext(accessor, _shareTunnelService);

        shareContext.SetPersona("Observant Falcon");
        Assert.Equal("Observant Falcon", shareContext.Persona);
    }

    [Fact]
    public void Persona_WithMachineIdCookie_ReturnsDeterministicPersona()
    {
        var context1 = new DefaultHttpContext();
        context1.Request.Headers["Cookie"] = "machineId=machine-42";
        var shareContext1 = new ShareContext(new HttpContextAccessor { HttpContext = context1 }, _shareTunnelService);

        var context2 = new DefaultHttpContext();
        context2.Request.Headers["Cookie"] = "machineId=machine-42";
        var shareContext2 = new ShareContext(new HttpContextAccessor { HttpContext = context2 }, _shareTunnelService);

        Assert.Equal(shareContext1.Persona, shareContext2.Persona);
    }

    [Fact]
    public void Persona_WithCookie_UsesCookiePersona()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Cookie"] = "tendril_persona=Wise%20Owl";

        var accessor = new HttpContextAccessor { HttpContext = context };
        var shareContext = new ShareContext(accessor, _shareTunnelService);

        Assert.Equal("Wise Owl", shareContext.Persona);
    }
}
