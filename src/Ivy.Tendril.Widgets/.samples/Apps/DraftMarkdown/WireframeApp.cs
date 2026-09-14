using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.PlanMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

[App(title: "Wireframes", icon: Icons.PenTool, group: ["DraftMarkdown"])]
class WireframeApp : ViewBase
{
    public override object Build()
    {
        var markdown = """
            # Checkout Redesign

            ## Wireframe

            ```wireframe
            name: checkout
            ```

            ```wireframe
            name: checkout
            viewport: Mobile
            ```

            ## Problem

            Returning customers retype their card on every order.

            ## Solution

            Step 2 of checkout adds saved cards beside the order summary, as the wireframes above show.
            On a phone the same step renders at phone width, scaled to fit the column.

            A block naming a wireframe this plan does not have:

            ```wireframe
            missing-screen
            ```

            A block that is not valid:

            ```wireframe
            name: Not A Slug
            ```
            """;

        return new DraftMarkdownWidget(markdown)
            .Article()
            .WireframeBaseUrl(WireframeSamples.BaseUrl);
    }
}
