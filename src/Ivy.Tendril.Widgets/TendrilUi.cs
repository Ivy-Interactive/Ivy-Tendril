using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Tendril.Widgets;

/*
 * The shared primitives of the widget bundle, exposed as external widgets so an Ivy app builds
 * the same tooltip, key hint, badge, icon button and agent status line the bundle's own widgets
 * render internally. The React side lives in `frontend/src/ui`.
 */

public enum TendrilTooltipSide
{
    Top,
    Right,
    Bottom,
    Left,
}

public enum TendrilBadgeKind
{
    Neutral,
    Project,
    Success,
    Warning,
    Danger,
}

/// <summary>A status dot's tones, which are not the badge kinds: it has no project tone, and
/// the badge has no info tone.</summary>
public enum TendrilDotTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

public enum TendrilIconButtonSize
{
    /// <summary>24px — for a control inside a dense row.</summary>
    Sm,

    /// <summary>28px — toolbars.</summary>
    Md,

    /// <summary>32px — headers and composers.</summary>
    Lg,
}

public enum TendrilIconButtonVariant
{
    Ghost,
    Danger,
    Solid,
}

/// <summary>Wraps its content in the app's one tooltip.</summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilTooltip",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilTooltip : WidgetBase<TendrilTooltip>
{
    public TendrilTooltip(object? content = null) : base(content != null ? [content] : [])
    {
    }

    /// <summary>The tooltip text. Nothing is shown while it is empty.</summary>
    [Prop] public string? Content { get; init; }

    /// <summary>Shortcut keys shown as a key cap, e.g. ["⌘", "K"].</summary>
    [Prop] public string[] Shortcut { get; init; } = [];

    [Prop] public TendrilTooltipSide Side { get; init; } = TendrilTooltipSide.Top;

    /// <summary>False renders the content without a tooltip.</summary>
    [Prop] public bool Enabled { get; init; } = true;

    /// <summary>Hover delay before the tooltip opens, in milliseconds.</summary>
    [Prop] public int DelayDuration { get; init; } = 500;
}

public static class TendrilTooltipExtensions
{
    public static TendrilTooltip Content(this TendrilTooltip w, string? content) =>
        w with { Content = content };

    public static TendrilTooltip Shortcut(this TendrilTooltip w, params string[] shortcut) =>
        w with { Shortcut = shortcut };

    public static TendrilTooltip Side(this TendrilTooltip w, TendrilTooltipSide side) =>
        w with { Side = side };

    public static TendrilTooltip Enabled(this TendrilTooltip w, bool enabled = true) =>
        w with { Enabled = enabled };

    public static TendrilTooltip DelayDuration(this TendrilTooltip w, int milliseconds) =>
        w with { DelayDuration = milliseconds };
}

/// <summary>A keyboard shortcut hint.</summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilKbd",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilKbd : WidgetBase<TendrilKbd>
{
    public TendrilKbd(params string[] keys)
    {
        Keys = keys;
    }

    [Prop] public string[] Keys { get; init; } = [];

    /// <summary>"boxed" draws a key cap; "bare" is the letters alone, inside a button's chrome.</summary>
    [Prop] public string Variant { get; init; } = "boxed";
}

public static class TendrilKbdExtensions
{
    public static TendrilKbd Keys(this TendrilKbd w, params string[] keys) => w with { Keys = keys };

    public static TendrilKbd Bare(this TendrilKbd w) => w with { Variant = "bare" };
}

/// <summary>A label badge, a count badge or a status dot — the app's notification indicators.</summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilBadge",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilBadge : WidgetBase<TendrilBadge>
{
    public TendrilBadge(string? label = null)
    {
        Label = label;
    }

    /// <summary>The badge's text, or the dot's accessible name.</summary>
    [Prop] public string? Label { get; init; }

    /// <summary>Renders a count instead of the label; a non-positive count renders nothing.</summary>
    [Prop] public int? Count { get; init; }

    /// <summary>Counts above this render as "99+".</summary>
    [Prop] public int Max { get; init; } = 99;

    [Prop] public TendrilBadgeKind Kind { get; init; } = TendrilBadgeKind.Neutral;

    /// <summary>Renders a status dot rather than a chip.</summary>
    [Prop] public bool Dot { get; init; }

    [Prop] public TendrilDotTone DotTone { get; init; } = TendrilDotTone.Neutral;

    /// <summary>Pulses the dot while something is in flight.</summary>
    [Prop] public bool Pulse { get; init; }
}

public static class TendrilBadgeExtensions
{
    public static TendrilBadge Label(this TendrilBadge w, string? label) => w with { Label = label };

    public static TendrilBadge Count(this TendrilBadge w, int? count) => w with { Count = count };

    public static TendrilBadge Max(this TendrilBadge w, int max) => w with { Max = max };

    public static TendrilBadge Kind(this TendrilBadge w, TendrilBadgeKind kind) => w with { Kind = kind };

    public static TendrilBadge Dot(this TendrilBadge w, TendrilDotTone tone, bool pulse = false) =>
        w with { Dot = true, DotTone = tone, Pulse = pulse };
}

/// <summary>A square icon button with the shared hover surface and tooltip.</summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilIconButton",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilIconButton : WidgetBase<TendrilIconButton>
{
    public TendrilIconButton(string label = "", string? icon = null)
    {
        Label = label;
        Icon = icon;
    }

    /// <summary>Accessible name, and the tooltip text unless <see cref="Tooltip"/> overrides it.</summary>
    [Prop] public string Label { get; init; } = "";

    /// <summary>A lucide icon name from the bundle's button set; unknown names render the child.</summary>
    [Prop] public string? Icon { get; init; }

    [Prop] public string? Tooltip { get; init; }

    [Prop] public string[] Shortcut { get; init; } = [];

    [Prop] public TendrilTooltipSide TooltipSide { get; init; } = TendrilTooltipSide.Top;

    [Prop] public TendrilIconButtonSize Size { get; init; } = TendrilIconButtonSize.Lg;

    [Prop] public TendrilIconButtonVariant Variant { get; init; } = TendrilIconButtonVariant.Ghost;

    [Prop] public bool Disabled { get; init; }

    /// <summary>Held-open look for a button whose panel is showing.</summary>
    [Prop] public bool Active { get; init; }

    [Event] public EventHandler<Event<TendrilIconButton>>? OnClick { get; init; }
}

public static class TendrilIconButtonExtensions
{
    public static TendrilIconButton Icon(this TendrilIconButton w, string? icon) => w with { Icon = icon };

    public static TendrilIconButton Tooltip(this TendrilIconButton w, string? tooltip) =>
        w with { Tooltip = tooltip };

    public static TendrilIconButton Shortcut(this TendrilIconButton w, params string[] shortcut) =>
        w with { Shortcut = shortcut };

    public static TendrilIconButton Size(this TendrilIconButton w, TendrilIconButtonSize size) =>
        w with { Size = size };

    public static TendrilIconButton Variant(this TendrilIconButton w, TendrilIconButtonVariant variant) =>
        w with { Variant = variant };

    public static TendrilIconButton Disabled(this TendrilIconButton w, bool disabled = true) =>
        w with { Disabled = disabled };

    public static TendrilIconButton Active(this TendrilIconButton w, bool active = true) =>
        w with { Active = active };

    public static TendrilIconButton OnClick(this TendrilIconButton w, Action handler) =>
        w with { OnClick = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}

/// <summary>
/// The agent status line: a spinner, the live elapsed time and token count, and the status
/// message — "12m 22s · 16.8k tokens · Waiting for Claude…".
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilStatusLine",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilStatusLine : WidgetBase<TendrilStatusLine>
{
    public TendrilStatusLine(string statusText = "Working…")
    {
        StatusText = statusText;
    }

    [Prop] public string StatusText { get; init; } = "Working…";

    [Prop] public bool IsComplete { get; init; }

    [Prop] public bool ShowIcon { get; init; } = true;

    /// <summary>When the run started; the elapsed time ticks against it once a second.</summary>
    [Prop] public DateTimeOffset? StartedAt { get; init; }

    /// <summary>A finished run's elapsed time, which freezes the timer.</summary>
    [Prop] public double? ElapsedMs { get; init; }

    [Prop] public int? Tokens { get; init; }

    /// <summary>Renders the token figure as an estimate ("~16.8k tokens").</summary>
    [Prop] public bool TokensEstimated { get; init; }
}

public static class TendrilStatusLineExtensions
{
    public static TendrilStatusLine StatusText(this TendrilStatusLine w, string text) =>
        w with { StatusText = text };

    public static TendrilStatusLine IsComplete(this TendrilStatusLine w, bool complete = true) =>
        w with { IsComplete = complete };

    public static TendrilStatusLine ShowIcon(this TendrilStatusLine w, bool showIcon = true) =>
        w with { ShowIcon = showIcon };

    public static TendrilStatusLine StartedAt(this TendrilStatusLine w, DateTimeOffset? startedAt) =>
        w with { StartedAt = startedAt };

    public static TendrilStatusLine Elapsed(this TendrilStatusLine w, TimeSpan elapsed) =>
        w with { ElapsedMs = elapsed.TotalMilliseconds };

    public static TendrilStatusLine Tokens(this TendrilStatusLine w, int? tokens, bool estimated = false) =>
        w with { Tokens = tokens, TokensEstimated = estimated };
}
