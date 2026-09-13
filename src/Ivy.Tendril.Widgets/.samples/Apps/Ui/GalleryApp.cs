using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.Ui;

/// <summary>
/// Every shared primitive in one place, so a change to the tooltip, the key hint, the badge, the
/// icon button or the agent status line can be checked against all of its variants at once.
/// </summary>
[App(title: "UI Primitives", icon: Icons.Shapes, group: ["Ui"])]
class GalleryApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var startedAt = UseState(() => DateTimeOffset.UtcNow.AddMinutes(-12).AddSeconds(-22));

        object Row(string title, params object[] items) =>
            Layout.Vertical().Gap(2)
                | Text.Muted(title)
                | (Layout.Horizontal().Gap(4) | items);

        return Layout.Vertical().Gap(8).Padding(6)
            | Text.H3("Tooltips")
            | Row(
                "Hover a trigger. Every tooltip in the bundle is this one.",
                new TendrilTooltip(Text.Muted("Plain label")).Content("A plain tooltip"),
                new TendrilTooltip(Text.Muted("With a shortcut"))
                    .Content("New plan")
                    .Shortcut("⌘", "⌥", "N"),
                new TendrilTooltip(Text.Muted("Placed right")).Content("On the right").Side(TendrilTooltipSide.Right))

            | Text.H3("Keyboard hints")
            | Row(
                "Boxed stands on its own; bare sits inside a button's chrome.",
                new TendrilKbd("⌘", "K"),
                new TendrilKbd("Ctrl", "Alt", "N"),
                new TendrilKbd("⌘", "K").Bare())

            | Text.H3("Badges, counts and dots")
            | Row(
                "One shape, tinted by kind.",
                new TendrilBadge("Project").Kind(TendrilBadgeKind.Project),
                new TendrilBadge("Passing").Kind(TendrilBadgeKind.Success),
                new TendrilBadge("Stale").Kind(TendrilBadgeKind.Warning),
                new TendrilBadge("Failed").Kind(TendrilBadgeKind.Danger),
                new TendrilBadge().Count(7),
                new TendrilBadge().Count(140),
                new TendrilBadge("Running").Dot(TendrilDotTone.Success, pulse: true),
                new TendrilBadge("Syncing").Dot(TendrilDotTone.Info))

            | Text.H3("Icon buttons")
            | Row(
                "Three sizes, three variants; the tooltip comes with the button.",
                new TendrilIconButton("Search", "Search").Size(TendrilIconButtonSize.Sm),
                new TendrilIconButton("Edit", "Pencil").Size(TendrilIconButtonSize.Md),
                new TendrilIconButton("New chat", "MessageSquarePlus")
                    .Shortcut("⌘", "N")
                    .OnClick(() => client.Toast("New chat", "OnClick").Info()),
                new TendrilIconButton("Delete", "Trash2").Variant(TendrilIconButtonVariant.Danger),
                new TendrilIconButton("Send", "Play").Variant(TendrilIconButtonVariant.Solid),
                new TendrilIconButton("Blocked", "Plus").Disabled().Tooltip("Nothing to add yet"))

            | Text.H3("Agent status line")
            | Row(
                "The elapsed time ticks against StartedAt.",
                new TendrilStatusLine("Waiting for Claude…")
                    .StartedAt(startedAt.Value)
                    .Tokens(16_800, estimated: true))
            | Row(
                "A finished run freezes at its reported figures.",
                new TendrilStatusLine("Completed")
                    .IsComplete()
                    .Elapsed(TimeSpan.FromSeconds(93))
                    .Tokens(21_412));
    }
}
