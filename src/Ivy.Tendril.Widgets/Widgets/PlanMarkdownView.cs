using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Widgets.PlanMarkdownView;

/// <summary>
/// Renders plan markdown in its own internal scroll container, alongside a
/// <c>FixedContent</c> slot that is pinned in place and unaffected by the
/// markdown scroll. Use the slot for interactive elements that should stay put
/// (to the right of the markdown) while the plan content scrolls.
/// </summary>
[ExternalWidget(
    "Widgets/frontend/dist/ivy-tendril-widgets.js",
    StylePath = "Widgets/frontend/dist/ivy-tendril-widgets.css",
    ExportName = "PlanMarkdownView",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("FixedContent")]
public record PlanMarkdownView : WidgetBase<PlanMarkdownView>
{
    public PlanMarkdownView(string content) : base()
    {
        Content = content;
    }

    internal PlanMarkdownView() { }

    /// <summary>The markdown source to render.</summary>
    [Prop] public string Content { get; init; } = string.Empty;

    /// <summary>Apply article-grade typography (heading spacing, h2 divider, relaxed line-height).</summary>
    [Prop] public bool Article { get; init; }

    /// <summary>Allow rendering of links to local files (e.g. file:// and relative artifact links).</summary>
    [Prop] public bool DangerouslyAllowLocalFiles { get; init; }

    /// <summary>Fired when a link inside the markdown is clicked; the payload is the href.</summary>
    [Event] public EventHandler<Event<PlanMarkdownView, string>>? OnLinkClick { get; init; }
}

public static class PlanMarkdownViewExtensions
{
    public static PlanMarkdownView Article(this PlanMarkdownView w, bool article = true) =>
        w with { Article = article };

    public static PlanMarkdownView DangerouslyAllowLocalFiles(this PlanMarkdownView w, bool allow = true) =>
        w with { DangerouslyAllowLocalFiles = allow };

    /// <summary>Sets the pinned (non-scrolling) content rendered to the right of the markdown.</summary>
    public static PlanMarkdownView FixedContent(this PlanMarkdownView w, object? content)
    {
        var others = w.Children.Where(c => c is not Slot s || s.Name != "FixedContent");
        var children = content != null
            ? others.Append(new Slot("FixedContent", content)).ToArray()
            : others.ToArray();
        return w with { Children = children };
    }

    public static PlanMarkdownView OnLinkClick(
        this PlanMarkdownView w,
        Func<Event<PlanMarkdownView, string>, ValueTask> handler
    ) => w with { OnLinkClick = new(handler) };

    public static PlanMarkdownView OnLinkClick(this PlanMarkdownView w, Action<string> handler) =>
        w with
        {
            OnLinkClick = new(e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }),
        };
}
