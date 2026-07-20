using Ivy;
using Ivy.Tendril.Widgets;
using TourPopoverWidget = Ivy.Tendril.Widgets.OnboardingTourPopover;

namespace WidgetSamples.Apps.OnboardingTourPopover;

[App(title: "Tour Popover", icon: Icons.MessageCircle, group: ["OnboardingTourPopover"])]
class DemoApp : ViewBase
{
    private static readonly (string Title, string Description, string Anchor, string Placement)[] Steps =
    [
        ("Create your first plan",
            "Describe a change you want made and Tendril turns it into a plan an agent can execute.",
            "[data-testid=\"demo-new-plan\"]", "right"),
        ("Refine it in Drafts",
            "New plans land in Drafts. Review the proposed steps, make adjustments, and start execution when it looks right.",
            "[data-testid=\"demo-drafts\"]", "right"),
        ("Follow along in Jobs",
            "Jobs shows everything Tendril is working on in the background — planning, executing and creating PRs.",
            "[data-testid=\"demo-jobs\"]", "right"),
        ("Accept work in Review",
            "Completed work lands in Review. Inspect the result and accept it, or send it back with change requests.",
            "[data-testid=\"demo-review\"]", "bottom")
    ];

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var step = UseState<int?>(0);

        var fakeSidebar = Layout.Vertical().Gap(2).Width(Size.Units(50))
                          | new Button("New Plan").Icon(Icons.Plus).Primary().Width(Size.Full()).TestId("demo-new-plan")
                          | new Button("Drafts").Icon(Icons.Feather).Ghost().Width(Size.Full()).TestId("demo-drafts")
                          | new Button("Jobs").Icon(Icons.Activity).Ghost().Width(Size.Full()).TestId("demo-jobs")
                          | new Button("Review").Icon(Icons.ThumbsUp).Ghost().Width(Size.Full()).TestId("demo-review")
                          | new Spacer().Height(Size.Units(8))
                          | new Button("Restart Tour").Outline().Width(Size.Full()).OnClick(() => step.Set(0));

        object? popover = null;
        if (step.Value is { } s)
        {
            var def = Steps[s];
            popover = new TourPopoverWidget(def.Anchor)
                .Title(def.Title)
                .Description(def.Description)
                .Step(s, Steps.Length)
                .Placement(def.Placement)
                .OnNext(() =>
                {
                    if (s >= Steps.Length - 1)
                    {
                        step.Set((int?)null);
                        client.Toast("Tour finished", "OnNext").Info();
                    }
                    else step.Set(s + 1);
                })
                .OnBack(() => step.Set(Math.Max(0, s - 1)))
                .OnDismiss(() =>
                {
                    step.Set((int?)null);
                    client.Toast("Tour dismissed", "OnDismiss").Info();
                });
        }

        return new Fragment(fakeSidebar, popover);
    }
}
