using System.Text.RegularExpressions;
using Ivy.Tendril.Wireframe.Project;

namespace Ivy.Tendril.Wireframe.Build;

public sealed record ClassWarning(
    string File,
    int Line,
    string ClassName,
    IReadOnlyList<string> Suggestions)
{
    public string Message
    {
        get
        {
            var hint = Suggestions.Count > 0
                ? $" Nearest available: {string.Join(", ", Suggestions)}."
                : "";

            var arbitrary = ClassName.Contains('[')
                ? " Arbitrary values are not generated; use style={{ ... }} instead."
                : "";

            return $"{File}:{Line}: `{ClassName}` produces no CSS.{hint}{arbitrary}";
        }
    }
}

/// <summary>
/// Reports class names that produce no CSS.
///
/// This is the piece that makes the precompiled utility sheet safe to rely on. Silent
/// no-op CSS is the worst failure mode for an author who cannot open devtools: the class
/// sits in the DOM, nothing happens, and nothing explains why. Turning that into a build
/// warning is the difference between an agent that converges and one that flails.
/// </summary>
public sealed partial class UtilityClassLinter(CssSelectorIndex index)
{
    /// <summary>
    /// Class names that are legitimately absent from the stylesheets. Component props like
    /// <c>className</c> pass through to elements that Tendril styles itself, and a few
    /// names are structural hooks rather than utilities.
    /// </summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "group", "peer", "dark", "tendril",
    };

    public IReadOnlyList<ClassWarning> Lint(WireframeProject project)
    {
        var warnings = new List<ClassWarning>();

        foreach (var file in project.SourceFiles())
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch (IOException) { continue; }

            var relative = project.RelativePath(file);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match literal in ClassNameLiteral().Matches(lines[i]))
                {
                    foreach (var token in literal.Groups[1].Value
                                 .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = token.Trim();
                        if (name.Length == 0 || Ignored.Contains(name)) continue;

                        // A template hole (`p-${size}`) cannot be checked statically.
                        if (name.Contains('$') || name.Contains('{')) continue;

                        if (index.Contains(name)) continue;

                        // For an arbitrary value the advice is "use an inline style", not
                        // "did you mean w-0" -- nearest-scale suggestions are pure noise
                        // when the author asked for a specific pixel value.
                        var suggestions = name.Contains('[')
                            ? []
                            : index.Suggest(name);

                        warnings.Add(new ClassWarning(relative, i + 1, name, suggestions));
                    }
                }
            }
        }

        return warnings;
    }

    // className="..." and class="..." with a plain string literal. Deliberately does not
    // try to evaluate clsx()/template strings -- a false positive is worse than a miss,
    // because it trains the reader to ignore the warnings.
    [GeneratedRegex("""class(?:Name)?\s*=\s*["']([^"'{}]*)["']""")]
    private static partial Regex ClassNameLiteral();
}
