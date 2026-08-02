using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Ivy.Tendril.Test.Commands;

public class ConsoleRedirectionTests
{
    [Fact]
    public void IsHandleRedirected_DiskFileHandle_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempFile = Path.GetTempFileName();
        try
        {
            using var fs = File.Create(tempFile);
            var handle = fs.SafeFileHandle.DangerousGetHandle();
            var result = Program.IsHandleRedirected(handle);
            Assert.True(result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsHandleRedirected_PipeHandle_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out);
        var handle = pipe.SafePipeHandle.DangerousGetHandle();
        var result = Program.IsHandleRedirected(handle);
        Assert.True(result);
    }

    [Fact]
    public void IsHandleRedirected_InvalidHandle_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = Program.IsHandleRedirected(IntPtr.Zero);
        Assert.False(result);
    }
}
