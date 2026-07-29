using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.TendrilSidebar;

[App(title: "Sidebar Demo", icon: Icons.PanelLeft, group: ["TendrilSidebar"])]
class DemoApp : ViewBase
{
    public record SidebarDemoModel(
        string ActiveItem = "dashboard",
        int DraftCount = 8,
        int ReviewCount = 3,
        int RecommendationsCount = 5,
        bool Collapsed = false
    );

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var model = UseState(() => new SidebarDemoModel());

        var sidebar = new Ivy.Tendril.Widgets.TendrilSidebar()
            .ActiveItem(model.Value.ActiveItem)
            .DraftCount(model.Value.DraftCount)
            .ReviewCount(model.Value.ReviewCount)
            .RecommendationsCount(model.Value.RecommendationsCount)
            .Collapsed(model.Value.Collapsed)
            .Jobs(
                new JobSubItem("acme", "acme", 5),
                new JobSubItem("geo-corp", "geo-corp"),
                new JobSubItem("untitled", "untitled", 2)
            )
            .OnSelect(item =>
            {
                model.Set(model.Value with { ActiveItem = item });
                client.Toast($"Selected item: {item}", "Navigation").Info();
            })
            .OnNewPlan(() => client.Toast("New Plan clicked", "Action").Success())
            .OnSelectAgent(() => client.Toast("Agent clicked", "Action").Info())
            .OnToggleCollapse(() =>
            {
                var newCollapsed = !model.Value.Collapsed;
                model.Set(model.Value with { Collapsed = newCollapsed });
                client.Toast($"Sidebar collapsed: {newCollapsed}", "Toggle").Info();
            });

        return new SidebarLayout(
            Layout.Vertical().Padding(20).Gap(10)
            | Text.H2($"Selected: {model.Value.ActiveItem}")
            | Text.P("Interact with the sidebar to see selection events and state changes.")
            ,
            sidebar
        ).Resizable();
    }
}
