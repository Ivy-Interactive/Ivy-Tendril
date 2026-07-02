using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class RevisionWriterTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _planFolder;
    private readonly ConfigService _config;

    public RevisionWriterTests()
    {
        var home = Path.Combine(_tempDir.Path, "home");
        Directory.CreateDirectory(home);
        _planFolder = Path.Combine(home, "Plans", "02369-SomePlan");
        Directory.CreateDirectory(_planFolder);

        _config = new ConfigService(new TendrilSettings(), home);
    }

    public void Dispose() => _tempDir.Dispose();

    [Fact]
    public void WriteNext_PolishesColonLineNumberSuffix_OnDisk()
    {
        var input = "See [jwt-tester.tsx:348](file:///D:/repo/src/jwt-tester.tsx:348).";

        var path = RevisionWriter.WriteNext(_planFolder, input, _config);

        var written = File.ReadAllText(path);
        Assert.Equal("See [jwt-tester.tsx:348](file:///D:/repo/src/jwt-tester.tsx).", written);
        Assert.EndsWith(Path.Combine("Revisions", "001.md"), path);
    }

    [Fact]
    public void WriteNext_IncrementsRevisionNumber()
    {
        var first = RevisionWriter.WriteNext(_planFolder, "first", _config);
        var second = RevisionWriter.WriteNext(_planFolder, "second", _config);

        Assert.EndsWith("001.md", first);
        Assert.EndsWith("002.md", second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void WriteNext_LeavesCleanContentUnchanged()
    {
        var clean = "A plain plan with no file links.";

        var path = RevisionWriter.WriteNext(_planFolder, clean, _config);

        Assert.Equal(clean, File.ReadAllText(path));
    }
}
