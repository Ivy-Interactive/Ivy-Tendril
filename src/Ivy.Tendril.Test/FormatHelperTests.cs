using System.Globalization;
using System.IO;
using System.Linq;
using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test;

/// <summary>
/// The app sets no <c>InvariantGlobalization</c> and never overrides the thread culture, so a
/// machine with a comma decimal separator would otherwise render a cost as "$1,2500" — a dollar
/// sign against a European decimal mark. These run on their own thread so the culture switch cannot
/// leak into another test running in parallel.
/// </summary>
public class FormatHelperTests
{
    private const string CommaDecimalCulture = "sv-SE";

    private static T InCulture<T>(string culture, Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result;
    }

    [Fact]
    public void FormatCost_CommaDecimalCulture_StillUsesAPeriod()
    {
        Assert.Equal("$1.25", InCulture(CommaDecimalCulture, () => FormatHelper.FormatCost(1.25m)));
    }

    [Fact]
    public void FormatCost_FourDecimals_ForPerJobFigures()
    {
        Assert.Equal("$1.2500", InCulture(CommaDecimalCulture, () => FormatHelper.FormatCost(1.25m, decimals: 4)));
    }

    [Fact]
    public void FormatCost_DefaultsToTwoDecimals()
    {
        Assert.Equal("$0.00", FormatHelper.FormatCost(0m));
        Assert.Equal("$12.45", FormatHelper.FormatCost(12.449m));
    }

    [Fact]
    public void FormatCount_CommaDecimalCulture_GroupsWithCommas()
    {
        Assert.Equal("1,234,567", InCulture(CommaDecimalCulture, () => FormatHelper.FormatCount(1_234_567)));
    }

    [Fact]
    public void FormatTokens_CommaDecimalCulture_StillUsesAPeriod()
    {
        // Abbreviated counts sit next to the costs in the same Jobs table; "1,5M" beside "$1.25"
        // would be two different decimal conventions in adjacent columns.
        Assert.Equal("1.5M", InCulture(CommaDecimalCulture, () => FormatHelper.FormatTokens(1_500_000)));
    }

    [Fact]
    public void FormatTokens_KeepsThresholds()
    {
        Assert.Equal("999", FormatHelper.FormatTokens(999));
        Assert.Equal("1K", FormatHelper.FormatTokens(1_000));
        Assert.Equal("1.0M", FormatHelper.FormatTokens(1_000_000));
    }

    // Shared by the plan details row and the job cost sheet, which read the profile from different
    // places (the plan's yaml and the job's launch record) but must label it the same way.
    [Theory]
    [InlineData("deep", "Deep")]
    [InlineData("balanced", "Balanced")]
    [InlineData("Deep", "Deep")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void FormatExecutionProfile(string? profile, string? expected)
    {
        Assert.Equal(expected, FormatHelper.FormatExecutionProfile(profile));
    }

    [Fact]
    public void SourceFiles_DoNotContainUtf8Bom()
    {
        var repoRoot = FindRepoRoot();
        var targetDirs = new[]
        {
            Path.Combine(repoRoot, "src", "Ivy.Tendril"),
            Path.Combine(repoRoot, "src", "Ivy.Tendril.Test")
        };

        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var violatingFiles = new List<string>();

        foreach (var dir in targetDirs)
        {
            var csFiles = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                            !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

            foreach (var file in csFiles)
            {
                using var stream = File.OpenRead(file);
                var header = new byte[3];
                var bytesRead = stream.Read(header, 0, 3);
                if (bytesRead == 3 && header.SequenceEqual(utf8Bom))
                {
                    violatingFiles.Add(Path.GetRelativePath(repoRoot, file));
                }
            }
        }

        Assert.Empty(violatingFiles);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Ivy.Tendril", "Ivy.Tendril.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from BaseDirectory: " + AppDomain.CurrentDomain.BaseDirectory);
    }
}
