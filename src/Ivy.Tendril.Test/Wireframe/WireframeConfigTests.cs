using Ivy.Tendril.Wireframe.Project;

namespace Ivy.Tendril.Test.Wireframe;

public class WireframeConfigTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wireframe-config-tests", Guid.NewGuid().ToString("N")[..10]);

    private WireframeProject Project
    {
        get
        {
            Directory.CreateDirectory(_root);
            return WireframeProject.At(_root);
        }
    }

    public void Dispose()
    {
        WireframeTempRoot.Remove(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Default_mode_is_the_embedded_superset() =>
        Assert.Equal(TailwindMode.Superset, WireframeConfig.Load(Project).Tailwind);

    [Fact]
    public void Default_config_writes_no_file()
    {
        // Nothing to record, so the project directory stays as clean as the scaffold left it.
        var project = Project;
        new WireframeConfig { Tailwind = TailwindMode.Superset }.Save(project);
        Assert.False(File.Exists(WireframeConfig.PathFor(project)));
    }

    [Fact]
    public void Jit_mode_round_trips_through_the_file()
    {
        var project = Project;
        new WireframeConfig { Tailwind = TailwindMode.Jit }.Save(project);

        // serve and screenshot read this back; if it does not persist they silently fall
        // back to the superset and arbitrary values stop working with no explanation.
        Assert.Equal(TailwindMode.Jit, WireframeConfig.Load(project).Tailwind);
        Assert.Contains("\"jit\"", File.ReadAllText(WireframeConfig.PathFor(project)));
    }

    [Fact]
    public void Switching_back_to_superset_removes_the_file()
    {
        var project = Project;
        new WireframeConfig { Tailwind = TailwindMode.Jit }.Save(project);
        new WireframeConfig { Tailwind = TailwindMode.Superset }.Save(project);
        Assert.False(File.Exists(WireframeConfig.PathFor(project)));
    }

    [Fact]
    public void A_broken_config_falls_back_rather_than_failing_the_render()
    {
        var project = Project;
        File.WriteAllText(WireframeConfig.PathFor(project), "{ not json");
        Assert.Equal(TailwindMode.Superset, WireframeConfig.Load(project).Tailwind);
    }

    [Theory]
    [InlineData("superset", TailwindMode.Superset)]
    [InlineData("SUPERSET", TailwindMode.Superset)]
    [InlineData("default", TailwindMode.Superset)]
    [InlineData("jit", TailwindMode.Jit)]
    [InlineData("Jit", TailwindMode.Jit)]
    [InlineData("standalone", TailwindMode.Jit)]
    public void Mode_parsing_accepts_the_obvious_spellings(string input, TailwindMode expected)
    {
        Assert.True(WireframeConfig.TryParseTailwind(input, out var mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void Unknown_mode_is_rejected_rather_than_silently_defaulted() =>
        Assert.False(WireframeConfig.TryParseTailwind("tailwind4", out _));
}
