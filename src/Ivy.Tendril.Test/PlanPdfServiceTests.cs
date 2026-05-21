using System.Diagnostics;
using System.Text;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PlanPdfServiceTests
{
    private static readonly bool PandocAvailable = CheckPandoc();

    private static bool CheckPandoc()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pandoc", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public void GeneratePdf_ShouldReturnNonEmptyBytes()
    {
        if (!PandocAvailable) return;

        var service = new PlanPdfService(NullLogger<PlanPdfService>.Instance);
        var result = service.GeneratePdf("Test Plan", 1, "# Test\n\nThis is a test plan.");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GeneratePdf_ShouldReturnValidPdfHeader()
    {
        if (!PandocAvailable) return;

        var service = new PlanPdfService(NullLogger<PlanPdfService>.Instance);
        var result = service.GeneratePdf("Test Plan", 1, "# Test\n\nThis is a test plan.");

        Assert.True(result.Length >= 4, "PDF should be at least 4 bytes");
        var header = Encoding.ASCII.GetString(result, 0, 4);
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public void GeneratePdf_ShouldHandleEmptyMarkdown()
    {
        if (!PandocAvailable) return;

        var service = new PlanPdfService(NullLogger<PlanPdfService>.Instance);
        var result = service.GeneratePdf("Empty Plan", 1, "");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        var header = Encoding.ASCII.GetString(result, 0, 4);
        Assert.Equal("%PDF", header);
    }
}