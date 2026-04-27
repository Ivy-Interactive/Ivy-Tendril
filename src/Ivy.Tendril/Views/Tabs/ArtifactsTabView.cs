using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Views.Tabs;

public class ArtifactsTabView(Dictionary<string, List<string>> artifacts) : ViewBase
{
    public override object Build()
    {
        var layout = Layout.Vertical().Gap(2);
        layout |= PlanContentHelpers.RenderArtifactScreenshots(artifacts);
        return layout;
    }
}
