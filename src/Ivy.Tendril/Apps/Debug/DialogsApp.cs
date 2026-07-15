namespace Ivy.Tendril.Apps.Debug;

// Debug-only harness for visually reviewing how the various Tendril dialogs render across
// different input permutations. Each dialog gets its own *DebugView, wrapped in an Expandable
// below — add a new entry here as more dialog harnesses are written.
[App(icon: Icons.Bug, isVisible: false)]
public class DialogsApp : ViewBase
{
    private record DialogTest(string Title, ViewBase View);

    private static DialogTest[] Tests() =>
    [
        new("DirtyRepoDialog", new DirtyRepoDialogDebugView()),
    ];

    public override object Build()
    {
        var content = Layout.Vertical().Gap(2);
        foreach (var test in Tests())
            content |= new Expandable(test.Title, test.View);

        return Layout.Vertical().Gap(6).Padding(8)
               | Text.H2("Dialog Test Harness")
               | Text.Muted("Expand a dialog to preview how it renders across input permutations.")
               | content;
    }
}
