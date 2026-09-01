using System.Collections.Immutable;

namespace Ivy.Tendril.Widgets;

public record MarkdownAnnotation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public string SelectedText { get; init; } = "";
    public string Comment { get; init; } = "";
    public string? Author { get; init; }
    public bool IsResolved { get; init; }
}

/// <summary>
/// A single question's answer, identified by the question's <c>id</c> in the <c>questions</c>
/// YAML schema. <c>null</c> or an empty <see cref="Answer" /> clears the question back to
/// unanswered (removing the <c>answer</c> key); a non-empty list is the answer itself — one entry
/// for a single-select or free-text question, several when the question's <c>multiple</c> is
/// <c>true</c>. There is no third state: a question that need not be answered is marked
/// <c>optional: true</c> where it is written.
/// <para>
/// Merging this back into the block's YAML is the host's job: find the question by <c>id</c> and
/// set or delete its <c>answer</c> key. <see cref="QuestionAnswers.Apply" /> does exactly that and
/// is what a host should reach for; <c>setAnswer</c> in <c>questionsSource.ts</c> is the same merge
/// expressed client-side.
/// </para>
/// </summary>
public sealed record QuestionAnswer(string QuestionId, IReadOnlyList<string>? Answer);

/// <summary>
/// Asks the widget to bring one question into view, so a host can put an index of them beside a
/// long plan.
/// <para>
/// <see cref="Token" /> is what makes the request repeatable: the widget scrolls when the target
/// changes, and clicking the same entry twice has to work. Bump it on every request — the id alone
/// would compare equal the second time and nothing would move.
/// </para>
/// </summary>
/// <param name="QuestionId">The question's <c>id</c>. Unknown ids are ignored.</param>
/// <param name="Token">Any value that differs from the previous request.</param>
public sealed record QuestionScrollTarget(string QuestionId, int Token);

/// <summary>
/// Renders plan markdown in its own internal scroll container, alongside a
/// <c>StickyContent</c> slot that is pinned in place and unaffected by the
/// markdown scroll. Use the slot for interactive elements that should stay put
/// (to the right of the markdown) while the plan content scrolls.
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "PlanMarkdown",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("StickyContent")]
public record PlanMarkdown : WidgetBase<PlanMarkdown>
{
    public PlanMarkdown(string content) : base()
    {
        Content = content;
    }

    internal PlanMarkdown() { }

    /// <summary>The markdown source to render.</summary>
    [Prop] public string Content { get; init; } = string.Empty;

    /// <summary>Apply article-grade typography (heading spacing, h2 divider, relaxed line-height).</summary>
    [Prop] public bool Article { get; init; } = true;

    /// <summary>Allow rendering of links to local files (e.g. file:// and relative artifact links).</summary>
    [Prop] public bool DangerouslyAllowLocalFiles { get; init; }

    /// <summary>Text annotations (highlights with comments) applied to the markdown content.</summary>
    [Prop] public ImmutableList<MarkdownAnnotation> Annotations { get; init; } = [];

    /// <summary>
    /// Scrolls the question with this id into view, block and all, whenever the value changes.
    /// Setting it does not re-render the markdown — only the scroll runs.
    /// </summary>
    [Prop] public QuestionScrollTarget? ScrollTo { get; init; }

    /// <summary>The current author/persona leaving annotations.</summary>
    [Prop] public string? CurrentAuthor { get; init; }

    /// <summary>Fired when a link inside the markdown is clicked; the payload is the href.</summary>
    [Event] public EventHandler<Event<PlanMarkdown, string>>? OnLinkClick { get; init; }

    /// <summary>Fired when annotations are added, edited, or removed.</summary>
    [Event] public EventHandler<Event<PlanMarkdown, List<MarkdownAnnotation>>>? OnAnnotationsChange { get; init; }

    /// <summary>
    /// Fired when the user answers, skips, or clears a question in a <c>questions</c> block.
    /// Subscribing is also what switches those blocks from a read-only callout to an interactive
    /// picker; a host that does not subscribe renders exactly as before.
    /// </summary>
    [Event] public EventHandler<Event<PlanMarkdown, QuestionAnswer>>? OnAnswersChange { get; init; }
}

[Obsolete("Use PlanMarkdown instead")]
public record DraftMarkdown : PlanMarkdown
{
    public DraftMarkdown(string content) : base(content) { }
    internal DraftMarkdown() { }
}

public static class PlanMarkdownExtensions
{
    public static PlanMarkdown CurrentAuthor(this PlanMarkdown w, string? author) =>
        w with { CurrentAuthor = author };
    public static PlanMarkdown Article(this PlanMarkdown w, bool article = true) =>
        w with { Article = article };

    public static PlanMarkdown DangerouslyAllowLocalFiles(this PlanMarkdown w, bool allow = true) =>
        w with { DangerouslyAllowLocalFiles = allow };

    /// <summary>Sets the pinned (non-scrolling) content rendered to the right of the markdown.</summary>
    public static PlanMarkdown StickyContent(this PlanMarkdown w, object? content)
    {
        var others = w.Children.Where(c => c is not Slot s || s.Name != "StickyContent");
        var children = content != null
            ? others.Append(new Slot("StickyContent", content)).ToArray()
            : others.ToArray();
        return w with { Children = children };
    }

    public static PlanMarkdown Annotations(this PlanMarkdown w, IEnumerable<MarkdownAnnotation> annotations) =>
        w with { Annotations = annotations.ToImmutableList() };

    /// <summary>Brings a question into view. See <see cref="QuestionScrollTarget" /> on repeat requests.</summary>
    public static PlanMarkdown ScrollTo(this PlanMarkdown w, QuestionScrollTarget? target) =>
        w with { ScrollTo = target };

    public static PlanMarkdown OnLinkClick(
        this PlanMarkdown w,
        Func<Event<PlanMarkdown, string>, ValueTask> handler
    ) => w with { OnLinkClick = new(handler) };

    public static PlanMarkdown OnLinkClick(this PlanMarkdown w, Action<string> handler) =>
        w with
        {
            OnLinkClick = new(e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }),
        };

    public static PlanMarkdown OnAnnotationsChange(
        this PlanMarkdown w,
        Func<Event<PlanMarkdown, List<MarkdownAnnotation>>, ValueTask> handler
    ) => w with { OnAnnotationsChange = new(handler) };

    public static PlanMarkdown OnAnnotationsChange(this PlanMarkdown w, Action<ImmutableList<MarkdownAnnotation>> handler) =>
        w with
        {
            OnAnnotationsChange = new(e =>
            {
                handler(e.Value.ToImmutableList());
                return ValueTask.CompletedTask;
            }),
        };

    public static PlanMarkdown OnAnswersChange(
        this PlanMarkdown w,
        Func<Event<PlanMarkdown, QuestionAnswer>, ValueTask> handler
    ) => w with { OnAnswersChange = new(handler) };

    public static PlanMarkdown OnAnswersChange(this PlanMarkdown w, Action<QuestionAnswer> handler) =>
        w with
        {
            OnAnswersChange = new(e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }),
        };
}
