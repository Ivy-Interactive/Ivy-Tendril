using System.Text;
using System.Text.RegularExpressions;
using Ivy.Tendril.Wireframe.Assets;

namespace Ivy.Tendril.Wireframe.Build;

/// <summary>
/// Every class name the active stylesheets actually define.
///
/// Built once from the shipped CSS so the linter can answer "does this class produce any
/// rule at all?" -- the question Tailwind never answers for you.
/// </summary>
public sealed partial class CssSelectorIndex
{
    private readonly HashSet<string> _classes;

    public int Count => _classes.Count;

    private CssSelectorIndex(HashSet<string> classes) => _classes = classes;

    public static CssSelectorIndex FromAssets(AssetCatalog assets)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sheet in new[] { "css/tendril.css", "css/wireframe-utilities.css" })
        {
            if (assets.Exists(sheet)) AddFrom(assets.ReadText(sheet), classes);
        }
        return new CssSelectorIndex(classes);
    }

    public bool Contains(string className) => _classes.Contains(className);

    /// <summary>
    /// Class names that differ only in their trailing scale step, e.g. gap-7 -> gap-6,
    /// gap-8. Nearly every miss is an off-by-one on a spacing scale, so this is the
    /// suggestion worth making.
    /// </summary>
    public IReadOnlyList<string> Suggest(string className, int limit = 3)
    {
        var dash = className.LastIndexOf('-');
        if (dash <= 0) return [];

        var prefix = className[..(dash + 1)];
        var suffix = className[(dash + 1)..];

        var siblings = _classes
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal) && c.Length > prefix.Length)
            .ToList();

        if (siblings.Count == 0) return [];

        // Numeric suffix: order by distance so "gap-7" offers 6 and 8, not 0 and 96.
        if (double.TryParse(suffix, System.Globalization.CultureInfo.InvariantCulture, out var target))
        {
            return siblings
                .Select(c => (Class: c, Value: double.TryParse(
                    c[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture, out var v)
                        ? v : double.MaxValue))
                .Where(x => x.Value != double.MaxValue)
                .OrderBy(x => Math.Abs(x.Value - target))
                .Take(limit)
                .Select(x => x.Class)
                .ToList();
        }

        return siblings.OrderBy(c => c, StringComparer.Ordinal).Take(limit).ToList();
    }

    /// <summary>
    /// Pulls class selectors out of CSS, unescaping the backslashes Tailwind uses for
    /// characters that are not valid in an identifier (<c>.w-1\/2</c>, <c>.md\:flex</c>).
    /// </summary>
    private static void AddFrom(string css, HashSet<string> classes)
    {
        foreach (Match match in ClassSelector().Matches(css))
        {
            var raw = match.Groups[1].Value;

            var sb = new StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length) i++;
                sb.Append(raw[i]);
            }

            var name = sb.ToString();
            if (name.Length > 0) classes.Add(name);
        }
    }

    // A class selector: a dot, then identifier characters plus the escapes Tailwind emits.
    [GeneratedRegex(@"\.((?:[A-Za-z0-9_-]|\\.)+)")]
    private static partial Regex ClassSelector();
}
