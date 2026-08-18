using Ivy.Tendril.Apps.Settings;

namespace Ivy.Tendril.Apps;

[App(title: "About", icon: Icons.Info, isVisible: false)]
public class AboutApp : ViewBase
{
    public override object Build() =>
        Layout.Vertical().Height(Size.Full()).Width(Size.Full())
        | new AboutSetupView();
}
