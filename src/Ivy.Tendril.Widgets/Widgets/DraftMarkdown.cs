using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Widgets.DraftMarkdown;

/// <summary>
/// Renders plan markdown in its own internal scroll container, alongside a
/// <c>FixedContent</c> slot that is pinned in place and unaffected by the
/// markdown scroll. Use the slot for interactive elements that should stay put
/// (to the right of the markdown) while the plan content scrolls.
/// </summary>
[ExternalWidget(
    "Widgets/frontend/dist/ivy-tendril-widgets.js",
    StylePath = "Widgets/frontend/dist/ivy-tendril-widgets.css",
    ExportName = "DraftMarkdown",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("FixedContent")]
public record DraftMarkdown : WidgetBase<DraftMarkdown>
{
    public DraftMarkdown(string content) : base()
    {
        Content = content;
    }

    internal DraftMarkdown() { }

    /// <summary>The markdown source to render.</summary>
    [Prop] public string Content { get; init; } = string.Empty;

    /// <summary>Apply article-grade typography (heading spacing, h2 divider, relaxed line-height).</summary>
    [Prop] public bool Article { get; init; }

    /// <summary>Allow rendering of links to local files (e.g. file:// and relative artifact links).</summary>
    [Prop] public bool DangerouslyAllowLocalFiles { get; init; }

    /// <summary>Fired when a link inside the markdown is clicked; the payload is the href.</summary>
    [Event] public EventHandler<Event<DraftMarkdown, string>>? OnLinkClick { get; init; }
}

public static class DraftMarkdownExtensions
{
    public static DraftMarkdown Article(this DraftMarkdown w, bool article = true) =>
        w with { Article = article };

    public static DraftMarkdown DangerouslyAllowLocalFiles(this DraftMarkdown w, bool allow = true) =>
        w with { DangerouslyAllowLocalFiles = allow };

    /// <summary>Sets the pinned (non-scrolling) content rendered to the right of the markdown.</summary>
    public static DraftMarkdown FixedContent(this DraftMarkdown w, object? content)
    {
        var others = w.Children.Where(c => c is not Slot s || s.Name != "FixedContent");
        var children = content != null
            ? others.Append(new Slot("FixedContent", content)).ToArray()
            : others.ToArray();
        return w with { Children = children };
    }

    public static DraftMarkdown OnLinkClick(
        this DraftMarkdown w,
        Func<Event<DraftMarkdown, string>, ValueTask> handler
    ) => w with { OnLinkClick = new(handler) };

    public static DraftMarkdown OnLinkClick(this DraftMarkdown w, Action<string> handler) =>
        w with
        {
            OnLinkClick = new(e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }),
        };
}
