using Ivy.Tendril.Apps.Settings;

namespace Ivy.Tendril.Apps;

[App(title: "Config Editor", icon: Icons.FileText, isVisible: false)]
public class ConfigEditorApp : ViewBase
{
    public override object Build() =>
        new RawConfigEditorView().WithLayout().Full().RemoveParentPadding();
}
