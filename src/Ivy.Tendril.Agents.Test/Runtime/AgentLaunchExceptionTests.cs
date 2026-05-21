using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class AgentLaunchExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsProperties()
    {
        var ex = new AgentLaunchException("claude", "Binary not found");

        Assert.Equal("claude", ex.AgentId);
        Assert.Equal("Binary not found", ex.Message);
        Assert.Null(ex.BinaryPath);
        Assert.Null(ex.Spec);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInner()
    {
        var inner = new FileNotFoundException("claude not found");
        var ex = new AgentLaunchException("claude", "Launch failed", inner);

        Assert.Same(inner, ex.InnerException);
        Assert.Equal("claude", ex.AgentId);
    }

    [Fact]
    public void Constructor_WithSpec_SetsSpecAndBinaryPath()
    {
        var spec = new AgentProcessSpec
        {
            FileName = "/usr/local/bin/claude",
            Arguments = ["--print"],
            WorkingDirectory = "/tmp",
            Environment = new Dictionary<string, string>(),
        };
        var inner = new InvalidOperationException("spawn failed");
        var ex = new AgentLaunchException("claude", spec, inner);

        Assert.Equal("claude", ex.AgentId);
        Assert.Same(spec, ex.Spec);
        Assert.Equal("/usr/local/bin/claude", ex.BinaryPath);
        Assert.Same(inner, ex.InnerException);
        Assert.Contains("spawn failed", ex.Message);
    }

    [Fact]
    public void IsException_CanBeCaught()
    {
        var ex = new AgentLaunchException("claude", "test");

        Assert.IsAssignableFrom<Exception>(ex);
    }
}
