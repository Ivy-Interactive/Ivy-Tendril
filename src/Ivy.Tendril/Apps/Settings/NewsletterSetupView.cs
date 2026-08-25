using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Settings;

public class NewsletterSetupView : ViewBase
{
    public override object Build()
    {
        return Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Newsletter").Bold()
               | Text.Block("Subscribe to the Ivy & Tendril newsletter to receive updates, feature highlights, and release notes.").Muted().Small()
               | new NewsletterView();
    }
}
