using Ivy.Tendril.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;

namespace Ivy.Tendril.Test.Commands;

public class AgentInstructionsCommandTests
{
    // Mirrors CliOutputTests.CaptureConsoleOut: the command writes via Console.Write, not
    // AnsiConsole, so we must swap Console.Out rather than the Spectre console.
    private static string CaptureConsoleOut(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static (Spectre.Console.Cli.CommandApp App, string TendrilHome) BuildApp()
    {
        var tendrilHome = Path.Combine(Path.GetTempPath(), $"agent-instructions-test-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSingleton<IConfigService>(new TestPlanConfigService(
            repoDir: Path.GetTempPath(),
            tendrilHome: tendrilHome));

        var app = Program.ConfigureCliCommands(services, new TestConsole());
        return (app, tendrilHome);
    }

    [Fact]
    public void AgentInstructions_WritesCompiledPromptToStdout_AndReturnsZero()
    {
        var (app, _) = BuildApp();
        int exitCode = 0;

        var output = CaptureConsoleOut(() => exitCode = app.Run(["agent-instructions"]));

        Assert.Equal(0, exitCode);
        Assert.StartsWith("# Tendril", output);
        Assert.NotEmpty(output);
    }

    [Fact]
    public void AgentInstructions_SubstitutesTendrilHomeAndPlanFolder()
    {
        var (app, tendrilHome) = BuildApp();
        var expectedHome = tendrilHome.Replace('\\', '/').TrimEnd('/');
        var expectedPlanFolder = Path.Combine(tendrilHome, "Plans").Replace('\\', '/').TrimEnd('/');

        var output = CaptureConsoleOut(() => app.Run(["agent-instructions"]));

        Assert.Contains(expectedHome, output);
        Assert.Contains(expectedPlanFolder, output);
        Assert.DoesNotContain("{TENDRIL_HOME}", output);
        Assert.DoesNotContain("{PLAN_FOLDER}", output);
    }

    [Fact]
    public void Compile_ReturnsPrompt_WhenEmbeddedResourceIsPresent()
    {
        var config = new TestPlanConfigService(
            repoDir: Path.GetTempPath(),
            tendrilHome: Path.Combine(Path.GetTempPath(), $"agent-instructions-test-{Guid.NewGuid():N}"));

        var prompt = AgentPromptCompiler.Compile(config);

        Assert.NotNull(prompt);
    }
}
