namespace LatexMarkdownFix;

[App(title: "Latex Markdown Fix", icon: Icons.Sigma)]
public class LatexMarkdownFixApp : ViewBase
{
    public override object? Build()
    {
        return Layout.Vertical().Gap(6).Padding(4)
            | Text.H3("Prose dollar signs (misrendered as math before the fix)")
            | Text.Markdown("bash expands any $ (e.g. $env:PORT, or vars like $IsMacOS) so $ survives")
            | Text.H3("Real math still works")
            | Text.Markdown("$$E = mc^2$$");
    }
}
