using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class SourceLinkRegistryTests
{
    private static SourceLinkRegistry NewRegistry(string pluginId = "plugin-a") =>
        new(NullLogger<SourceLinkRegistry>.Instance, () => pluginId);

    [Fact]
    public void TryGetLabel_NoResolvers_ReturnsNull()
    {
        Assert.Null(NewRegistry().TryGetLabel("https://linear.app/ivy/issue/IVY-456/slug"));
    }

    [Fact]
    public void TryGetLabel_ResolverRecognizesUrl_ReturnsLabel()
    {
        var registry = NewRegistry();
        registry.RegisterResolver(url => url.Host == "linear.app" ? "IVY-456" : null);

        Assert.Equal("IVY-456", registry.TryGetLabel("https://linear.app/ivy/issue/IVY-456/slug"));
    }

    [Fact]
    public void TryGetLabel_ResolverDeclinesUrl_ReturnsNull()
    {
        var registry = NewRegistry();
        registry.RegisterResolver(url => url.Host == "linear.app" ? "IVY-456" : null);

        Assert.Null(registry.TryGetLabel("https://github.com/ivy/repo/issues/123"));
    }

    [Fact]
    public void TryGetLabel_MultipleResolvers_FirstMatchWins()
    {
        var registry = NewRegistry();
        registry.RegisterResolver(_ => null);
        registry.RegisterResolver(_ => "SECOND");
        registry.RegisterResolver(_ => "THIRD");

        Assert.Equal("SECOND", registry.TryGetLabel("https://example.com/x"));
    }

    [Fact]
    public void TryGetLabel_ResolverThrows_IsSkippedNotPropagated()
    {
        var registry = NewRegistry();
        registry.RegisterResolver(_ => throw new InvalidOperationException("bad regex"));
        registry.RegisterResolver(_ => "SURVIVED");

        Assert.Equal("SURVIVED", registry.TryGetLabel("https://example.com/x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]      // parses as an absolute file: URI on Unix — must not reach resolvers
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    public void TryGetLabel_UnusableUrl_ReturnsNullWithoutCallingResolvers(string? url)
    {
        var registry = NewRegistry();
        var called = false;
        registry.RegisterResolver(_ =>
        {
            called = true;
            return "LABEL";
        });

        Assert.Null(registry.TryGetLabel(url));
        Assert.False(called);
    }

    [Fact]
    public void TryGetLabel_BlankAndWhitespaceLabels_AreTreatedAsDeclined()
    {
        var registry = NewRegistry();
        registry.RegisterResolver(_ => "");
        registry.RegisterResolver(_ => "   ");
        registry.RegisterResolver(_ => "  IVY-1  ");

        Assert.Equal("IVY-1", registry.TryGetLabel("https://example.com/x"));
    }

    [Fact]
    public void RemovePluginResolvers_DropsOnlyThatPluginsResolvers()
    {
        string currentPlugin = "plugin-a";
        var registry = new SourceLinkRegistry(NullLogger<SourceLinkRegistry>.Instance, () => currentPlugin);

        registry.RegisterResolver(_ => "FROM-A");
        currentPlugin = "plugin-b";
        registry.RegisterResolver(_ => "FROM-B");

        registry.RemovePluginResolvers("plugin-a");

        Assert.Equal("FROM-B", registry.TryGetLabel("https://example.com/x"));

        registry.RemovePluginResolvers("plugin-b");

        Assert.Null(registry.TryGetLabel("https://example.com/x"));
    }
}
