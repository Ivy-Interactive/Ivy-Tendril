namespace Ivy.Tendril.Widgets;

/// <summary>
/// Floating onboarding-tour card rendered in a portal on top of the app, with a
/// bubble pointer aimed at the element matched by <see cref="AnchorSelector"/>.
/// The widget renders nothing inline, so it never affects the layout of the view
/// it is placed in; the anchor is located globally via <c>document.querySelector</c>.
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "OnboardingTourPopover",
    GlobalName = "IvyTendrilWidgets"
)]
public record OnboardingTourPopover : WidgetBase<OnboardingTourPopover>
{
    public OnboardingTourPopover(string anchorSelector)
    {
        AnchorSelector = anchorSelector;
    }

    internal OnboardingTourPopover() { }

    /// <summary>CSS selector (searched document-wide) for the element the pointer aims at.</summary>
    [Prop] public string AnchorSelector { get; init; } = string.Empty;

    [Prop] public string Title { get; init; } = string.Empty;

    [Prop] public string Description { get; init; } = string.Empty;

    /// <summary>Zero-based index of the current step; shown as "{StepIndex + 1} of {StepCount}".</summary>
    [Prop] public int StepIndex { get; init; }

    [Prop] public int StepCount { get; init; } = 1;

    /// <summary>Side of the anchor the card is placed on: right, left, top or bottom. Flips automatically when there is no room.</summary>
    [Prop] public string Placement { get; init; } = "right";

    /// <summary>Draw a rounded highlight ring around the anchor element.</summary>
    [Prop] public bool HighlightAnchor { get; init; } = true;

    /// <summary>Fired by the Continue button (also on the last step, where it is labelled "Done").</summary>
    [Event] public EventHandler<Event<OnboardingTourPopover>>? OnNext { get; init; }

    /// <summary>Fired by the Back button on steps after the first.</summary>
    [Event] public EventHandler<Event<OnboardingTourPopover>>? OnBack { get; init; }

    /// <summary>Fired by the close (X) button and by Skip on the first step.</summary>
    [Event] public EventHandler<Event<OnboardingTourPopover>>? OnDismiss { get; init; }
}

public static class OnboardingTourPopoverExtensions
{
    public static OnboardingTourPopover Title(this OnboardingTourPopover w, string title) =>
        w with { Title = title };

    public static OnboardingTourPopover Description(this OnboardingTourPopover w, string description) =>
        w with { Description = description };

    public static OnboardingTourPopover Step(this OnboardingTourPopover w, int stepIndex, int stepCount) =>
        w with { StepIndex = stepIndex, StepCount = stepCount };

    public static OnboardingTourPopover Placement(this OnboardingTourPopover w, string placement) =>
        w with { Placement = placement };

    public static OnboardingTourPopover HighlightAnchor(this OnboardingTourPopover w, bool highlight = true) =>
        w with { HighlightAnchor = highlight };

    public static OnboardingTourPopover OnNext(this OnboardingTourPopover w, Action handler) =>
        w with { OnNext = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static OnboardingTourPopover OnBack(this OnboardingTourPopover w, Action handler) =>
        w with { OnBack = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static OnboardingTourPopover OnDismiss(this OnboardingTourPopover w, Action handler) =>
        w with { OnDismiss = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
