using System.Collections.Immutable;
using Ivy.Tendril.Apps.ReviewAction;

namespace Ivy.Tendril.Test.Apps;

// A review action becomes a WebViewer the moment the app it started says where it is serving,
// so what counts as "where it is serving" is the whole hinge of the feature: miss it and the
// reviewer is left in a terminal, get it wrong and they are looking at a documentation page.
public class AppPreviewTests
{
    [Theory]
    [InlineData("Ivy is running on https://localhost:5011 [30668]", "https://localhost:5011")]
    [InlineData("  ➜  Local:   http://localhost:5173/", "http://localhost:5173/")]
    [InlineData("Now listening on: http://127.0.0.1:5000", "http://127.0.0.1:5000")]
    [InlineData("started at http://[::1]:8080/", "http://[::1]:8080/")]
    public void DetectUrl_FindsWhatADevServerPrints(string transcript, string expected)
    {
        Assert.Equal(expected, AppPreview.DetectUrl(transcript));
    }

    // Terminal output wraps URLs in prose; the punctuation around one is not part of it.
    [Theory]
    [InlineData("serving on http://localhost:3000.", "http://localhost:3000")]
    [InlineData("open the app (http://localhost:3000)", "http://localhost:3000")]
    [InlineData("see <http://localhost:3000>", "http://localhost:3000")]
    public void DetectUrl_TrimsTrailingPunctuation(string transcript, string expected)
    {
        Assert.Equal(expected, AppPreview.DetectUrl(transcript));
    }

    // A build prints plenty of URLs that are not the app. None of them name a port.
    [Theory]
    [InlineData("")]
    [InlineData("error NU1101: see https://aka.ms/nuget-error for details")]
    [InlineData("Restored from https://api.nuget.org/v3/index.json")]
    [InlineData("For help, visit https://vitejs.dev/guide/")]
    public void DetectUrl_IgnoresUrlsThatCannotBeADevServer(string transcript)
    {
        Assert.Null(AppPreview.DetectUrl(transcript));
    }

    [Fact]
    public void DetectUrl_PrefersLoopbackOverTheNetworkAddress()
    {
        const string transcript = """
              ➜  Network: http://192.168.1.9:5173/
              ➜  Local:   http://localhost:5173/
            """;

        Assert.Equal("http://localhost:5173/", AppPreview.DetectUrl(transcript));
    }

    // Bound to 0.0.0.0, a dev server prints only its LAN address — still the app.
    [Fact]
    public void DetectUrl_TakesANetworkAddressWhenThereIsNoLoopbackOne()
    {
        Assert.Equal("http://192.168.1.9:5173/", AppPreview.DetectUrl("  ➜  Network: http://192.168.1.9:5173/"));
    }

    [Theory]
    [InlineData("http://localhost:5173/", true)]
    [InlineData("http://127.0.0.1:5000/", true)]
    [InlineData("http://[::1]:8080/", true)]
    [InlineData("http://192.168.1.9:5173/", true)]
    [InlineData("http://10.1.2.3:3000/", true)]
    [InlineData("http://172.16.0.4:3000/", true)]
    [InlineData("http://172.31.255.1:3000/", true)]
    [InlineData("http://169.254.1.1/", true)]
    [InlineData("http://172.32.0.1:3000/", false)]
    [InlineData("http://8.8.8.8:3000/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("https://ivy.app/", false)]
    public void IsLocalTarget_AdmitsThisMachineAndItsNetworkAndNothingElse(string url, bool expected)
    {
        Assert.Equal(expected, AppPreview.IsLocalTarget(new Uri(url)));
    }

    [Fact]
    public void SourceLabel_ReadsTheResolvedFileAndLine()
    {
        const string debug = """{"source":{"file":"src/App.tsx","line":59,"col":12},"confidence":"high"}""";

        Assert.Equal("src/App.tsx:59", AppPreview.SourceLabel(debug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"source":null}""")]
    [InlineData("""{"frames":[{"url":"https://x/a.js","line":1,"col":2}]}""")]
    public void SourceLabel_IsNullWhenNothingResolved(string? debugJson)
    {
        Assert.Null(AppPreview.SourceLabel(debugJson));
    }

    [Fact]
    public void FormatChangeRequest_LeadsWithTheSourceLocationWhenThereIsOne()
    {
        var comments = ImmutableList.Create(
            new AppComment("c1", 1, "button", "main > button", "Make this green",
                """{"source":{"file":"src/App.tsx","line":59}}"""),
            new AppComment("c2", 2, "div", "main > div.hero", "Too much padding", null));

        var request = AppPreview.FormatChangeRequest("http://localhost:5173/", comments);

        Assert.Contains("http://localhost:5173/", request);
        Assert.Contains("**1. <button>** in `src/App.tsx:59`", request);
        Assert.Contains("Make this green", request);
        // Nothing resolved for the second one, so the selector is all the agent gets.
        Assert.Contains("**2. <div>**", request);
        Assert.DoesNotContain("**2. <div>** in", request);
        Assert.Contains("main > div.hero", request);
    }
}
