using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class TerminalLinkHelperTests : IDisposable
{
    public TerminalLinkHelperTests()
    {
        TerminalLinkHelper.BrowserLauncher = null;
        TerminalLinkHelper.FileLauncher = null;
        TerminalLinkHelper.DefaultPlanHandler = null;
    }

    public void Dispose()
    {
        TerminalLinkHelper.BrowserLauncher = null;
        TerminalLinkHelper.FileLauncher = null;
        TerminalLinkHelper.DefaultPlanHandler = null;
    }

    [Theory]
    [InlineData("http://localhost:5173/")]
    [InlineData("http://localhost:3000/dashboard")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://github.com/ivy-interactive/ivy-tendril")]
    [InlineData("https://docs.ivy.app/widgets/xterm")]
    public void TryOpenTerminalLink_ValidHttpAndHttpsUrls_LaunchesBrowser(string url)
    {
        string? launchedUrl = null;
        var result = TerminalLinkHelper.TryOpenTerminalLink(
            url,
            browserLauncher: target => launchedUrl = target);

        Assert.True(result);
        Assert.Equal(url, launchedUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("cmd:calc.exe")]
    [InlineData("powershell:Start-Process calc")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("data:text/html,<html>test</html>")]
    [InlineData("mailto:test@example.com")]
    [InlineData("ssh://user@host")]
    public void TryOpenTerminalLink_UnsupportedOrMaliciousSchemes_RejectedAndNotLaunched(string url)
    {
        var launched = false;
        var result = TerminalLinkHelper.TryOpenTerminalLink(
            url,
            browserLauncher: _ => launched = true,
            fileLauncher: _ => launched = true);

        Assert.False(result);
        Assert.False(launched);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("http//missing-colon")]
    [InlineData("relative/path/to/file.txt")]
    [InlineData("::bad-uri::")]
    public void TryOpenTerminalLink_InvalidAndUnparseableStrings_ReturnsFalseWithoutThrowing(string? url)
    {
        var launched = false;
        var result = TerminalLinkHelper.TryOpenTerminalLink(
            url,
            browserLauncher: _ => launched = true,
            fileLauncher: _ => launched = true);

        Assert.False(result);
        Assert.False(launched);
    }

    [Fact]
    public void TryOpenTerminalLink_PlanScheme_InvokesCallbackWithParsedId()
    {
        var planIdCaptured = 0;
        var result = TerminalLinkHelper.TryOpenTerminalLink(
            "plan://00381",
            onPlanClick: id => planIdCaptured = id);

        Assert.True(result);
        Assert.Equal(381, planIdCaptured);
    }

    [Fact]
    public void TryOpenTerminalLink_PlanSchemeWithLeadingZerosAndTrailingSlash_ParsesCorrectly()
    {
        var planIdCaptured = 0;
        var result = TerminalLinkHelper.TryOpenTerminalLink(
            "plan://01234/",
            onPlanClick: id => planIdCaptured = id);

        Assert.True(result);
        Assert.Equal(1234, planIdCaptured);
    }

    [Fact]
    public void TryOpenTerminalLink_PlanSchemeWithoutCallback_ReturnsFalse()
    {
        var result = TerminalLinkHelper.TryOpenTerminalLink("plan://00381");

        Assert.False(result);
    }

    [Fact]
    public void TryOpenTerminalLink_PlanSchemeWithInvalidId_ReturnsFalse()
    {
        var planIdCaptured = 0;
        var result = TerminalLinkHelper.TryOpenTerminalLink(
            "plan://notanumber",
            onPlanClick: id => planIdCaptured = id);

        Assert.False(result);
        Assert.Equal(0, planIdCaptured);
    }

    [Fact]
    public void TryOpenTerminalLink_ExistingFileUri_LaunchesFileViewer()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            string? openedPath = null;
            var fileUri = new Uri(tempFile).AbsoluteUri;

            var result = TerminalLinkHelper.TryOpenTerminalLink(
                fileUri,
                fileLauncher: path => openedPath = path);

            Assert.True(result);
            Assert.NotNull(openedPath);
            Assert.True(File.Exists(openedPath));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void TryOpenTerminalLink_NonExistentFileUri_RejectedAndNotLaunched()
    {
        var launched = false;
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_file_{Guid.NewGuid():N}.txt");
        var fileUri = new Uri(nonExistentPath).AbsoluteUri;

        var result = TerminalLinkHelper.TryOpenTerminalLink(
            fileUri,
            fileLauncher: _ => launched = true);

        Assert.False(result);
        Assert.False(launched);
    }

    [Fact]
    public void OpenTerminalLink_VoidOverload_SafelyIgnoresInvalidInputWithoutThrowing()
    {
        var exception = Record.Exception(() =>
        {
            TerminalLinkHelper.OpenTerminalLink(null);
            TerminalLinkHelper.OpenTerminalLink("");
            TerminalLinkHelper.OpenTerminalLink("not a valid url");
            TerminalLinkHelper.OpenTerminalLink("javascript:void(0)");
        });

        Assert.Null(exception);
    }

    [Fact]
    public void OpenTerminalLink_WhenLauncherThrows_CatchesAndSuppressesException()
    {
        TerminalLinkHelper.BrowserLauncher = _ => throw new InvalidOperationException("Browser crashed");

        var exception = Record.Exception(() =>
        {
            TerminalLinkHelper.OpenTerminalLink("http://localhost:5173/");
        });

        Assert.Null(exception);
    }
}
