using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ivy.Tendril.Test.Commands;

[TestClass]
public class ConsoleRedirectionTests
{
    [TestMethod]
    public void IsHandleRedirected_DiskFileHandle_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempFile = Path.GetTempFileName();
        try
        {
            using var fs = File.Create(tempFile);
            var handle = fs.SafeFileHandle.DangerousGetHandle();
            var result = Program.IsHandleRedirected(handle);
            Assert.IsTrue(result, "A disk file handle should be detected as redirected");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void IsHandleRedirected_PipeHandle_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out);
        var handle = pipe.SafePipeHandle.DangerousGetHandle();
        var result = Program.IsHandleRedirected(handle);
        Assert.IsTrue(result, "A pipe handle should be detected as redirected");
    }

    [TestMethod]
    public void IsHandleRedirected_InvalidHandle_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = Program.IsHandleRedirected(IntPtr.Zero);
        Assert.IsFalse(result, "An invalid handle should not be detected as redirected");
    }
}
