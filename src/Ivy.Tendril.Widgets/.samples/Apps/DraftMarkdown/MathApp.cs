using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

[App(title: "Math", icon: Icons.Sigma, group: ["DraftMarkdown"])]
class MathApp : ViewBase
{
    public override object Build()
    {
        var markdown = """
            # Math Expressions

            ## Inline Math
            Math written as `$$...$$` inside a paragraph renders inline: the relation
            $$E = mc^2$$ holds for any rest mass, and the golden ratio
            $$\varphi = \frac{1 + \sqrt{5}}{2}$$ appears throughout geometry.

            ## Display Math
            A `$$...$$` block on its own lines renders centered:

            $$
            \sum_{i=1}^{n} i = \frac{n(n+1)}{2}
            $$

            $$
            \int_{-\infty}^{\infty} e^{-x^2} \, dx = \sqrt{\pi}
            $$

            ## Matrices and Alignment

            $$
            A = \begin{pmatrix}
            a & b \\
            c & d
            \end{pmatrix}
            \qquad
            \det(A) = ad - bc
            $$

            ## Prose Dollar Signs
            Single dollar signs are never treated as math, so shell variables and prices
            survive verbatim: bash expands any $ (e.g. $env:PORT, or vars like $IsMacOS)
            so $ survives, and this widget costs $0.

            Dollars inside code are untouched too: `$env:PORT`, and

            ```bash
            echo $PATH
            export PORT=$PORT
            ```

            A lone $x^2$ with single dollars stays literal text rather than becoming math.

            ## Malformed Math
            Unparseable TeX renders as a red inline error instead of breaking the page:

            $$\frac{a}{$$
            """;

        return new DraftMarkdownWidget(markdown).Article();
    }
}
