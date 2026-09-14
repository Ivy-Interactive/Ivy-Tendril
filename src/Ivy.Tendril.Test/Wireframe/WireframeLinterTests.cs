using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Build;
using Ivy.Tendril.Wireframe.Project;

namespace Ivy.Tendril.Test.Wireframe;

/// <summary>
/// The linter is what makes the precompiled utility sheet safe: it converts Tailwind's
/// worst property, a class that silently produces nothing, into a visible warning.
/// </summary>
public class WireframeLinterTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wireframe-lint-tests", Guid.NewGuid().ToString("N")[..10]);

    private static readonly CssSelectorIndex Index = CssSelectorIndex.FromAssets(AssetCatalog.Default);

    public void Dispose()
    {
        WireframeTempRoot.Remove(_root);
        GC.SuppressFinalize(this);
    }

    private WireframeProject ProjectWith(string appTsx)
    {
        var project = WireframeProject.At(_root);
        Directory.CreateDirectory(project.SourceDir);
        File.WriteAllText(Path.Combine(project.SourceDir, "App.tsx"), appTsx);
        return project;
    }

    private IReadOnlyList<ClassWarning> Lint(string appTsx) =>
        new UtilityClassLinter(Index).Lint(ProjectWith(appTsx));

    [Fact]
    public void Index_covers_both_shipped_stylesheets()
    {
        Assert.True(Index.Count > 5000);
        Assert.True(Index.Contains("grid-cols-3"));   // from the utility superset
        Assert.True(Index.Contains("tendril"));       // from the library sheet
    }

    [Fact]
    public void Escaped_selectors_are_unescaped_so_variants_and_fractions_match()
    {
        // Tailwind writes these as .md\:flex and .w-1\/2 in the CSS.
        Assert.True(Index.Contains("md:flex"));
        Assert.True(Index.Contains("w-1/2"));
        Assert.True(Index.Contains("hover:bg-paper"));
    }

    [Fact]
    public void Valid_classes_produce_no_warnings()
    {
        var warnings = Lint("""
            export default () => <div className="grid grid-cols-3 gap-6 p-8 mx-auto max-w-4xl" />;
            """);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Off_scale_class_is_reported_with_the_nearest_steps()
    {
        var warnings = Lint("""
            export default () => <div className="mt-13" />;
            """);

        var warning = Assert.Single(warnings);
        Assert.Equal("mt-13", warning.ClassName);
        Assert.Equal(1, warning.Line);
        Assert.Contains("mt-12", warning.Suggestions);
        Assert.Contains("mt-14", warning.Suggestions);
    }

    [Fact]
    public void Arbitrary_value_is_reported_with_the_inline_style_hint_and_no_scale_noise()
    {
        var warnings = Lint("""
            export default () => <div className="w-[347px]" />;
            """);

        var warning = Assert.Single(warnings);
        Assert.Empty(warning.Suggestions);              // "did you mean w-0" helps nobody
        Assert.Contains("style={{", warning.Message);
    }

    [Fact]
    public void Template_interpolation_is_skipped_rather_than_guessed_at()
    {
        // A false positive trains the reader to ignore the warnings, which is worse than
        // missing one.
        var warnings = Lint("""
            export default ({ n }) => <div className={`p-${n} gap-${n}`} />;
            """);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Structural_hooks_are_not_flagged()
    {
        var warnings = Lint("""
            export default () => <div className="group peer tendril" />;
            """);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Line_numbers_point_at_the_offending_line()
    {
        var warnings = Lint("""
            export default () => (
              <div>
                <span className="zzz-not-a-class" />
              </div>
            );
            """);

        var warning = Assert.Single(warnings);
        Assert.Equal(3, warning.Line);
        Assert.Contains("App.tsx:3", warning.Message);
    }
}
