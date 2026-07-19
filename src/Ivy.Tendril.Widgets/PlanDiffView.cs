namespace Ivy.Tendril.Widgets;

public enum DiffViewType
{
    Unified,
    Split
}

public record DraftComment(
    string FilePath,
    string ChangeKey,
    string Content,
    int LineNumber
);

public record DirectEditArgs(
    string FilePath,
    int LineNumber,
    string NewContent,
    string CommitMessage
);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "PlanDiffView",
    GlobalName = "IvyTendrilWidgets"
)]
public record PlanDiffView : WidgetBase<PlanDiffView>
{
    /// <summary>The raw git diff content to parse and render.</summary>
    [Prop] public string? Diff { get; init; }

    /// <summary>Programming language for syntax highlighting.</summary>
    [Prop] public string? Language { get; init; }

    /// <summary>Rendering style: Unified or Split (side-by-side).</summary>
    [Prop] public DiffViewType ViewType { get; init; } = DiffViewType.Unified;

    /// <summary>Optional identifier for the old/source revision.</summary>
    [Prop] public string? OldRevision { get; init; }

    /// <summary>Optional identifier for the new/target revision.</summary>
    [Prop] public string? NewRevision { get; init; }

    /// <summary>Wrap long lines of code in the diff viewer.</summary>
    [Prop] public bool WordWrap { get; init; } = true;

    /// <summary>Allow collapsing the entire file diff viewer block.</summary>
    [Prop] public bool Collapsible { get; init; } = false;

    /// <summary>Whether the diff starts in a collapsed state (only applies when Collapsible is true).</summary>
    [Prop] public bool DefaultCollapsed { get; init; } = false;

    /// <summary>Draft comments on the diff</summary>
    [Prop] public List<DraftComment>? Comments { get; init; }

    /// <summary>The file path of the diff</summary>
    [Prop] public string? FilePath { get; init; }

    [Event] public Func<Event<PlanDiffView, int>, ValueTask>? OnLineClick { get; init; }

    [Event] public Func<Event<PlanDiffView, DraftComment>, ValueTask>? OnAddComment { get; init; }

    [Event] public Func<Event<PlanDiffView, DraftComment>, ValueTask>? OnDeleteComment { get; init; }

    [Event] public Func<Event<PlanDiffView, DraftComment>, ValueTask>? OnUpdateComment { get; init; }

    [Event] public Func<Event<PlanDiffView, DirectEditArgs>, ValueTask>? OnDirectEdit { get; init; }
}

public static class PlanDiffViewExtensions
{
    public static PlanDiffView Diff(this PlanDiffView w, string? diff) =>
        w with { Diff = diff };

    public static PlanDiffView Language(this PlanDiffView w, string? language) =>
        w with { Language = language };

    public static PlanDiffView ViewType(this PlanDiffView w, DiffViewType type) =>
        w with { ViewType = type };

    public static PlanDiffView OldRevision(this PlanDiffView w, string? revision) =>
        w with { OldRevision = revision };

    public static PlanDiffView NewRevision(this PlanDiffView w, string? revision) =>
        w with { NewRevision = revision };

    public static PlanDiffView WordWrap(this PlanDiffView w, bool wrap = true) =>
        w with { WordWrap = wrap };

    public static PlanDiffView Collapsible(this PlanDiffView w, bool collapsible = true) =>
        w with { Collapsible = collapsible };

    public static PlanDiffView DefaultCollapsed(this PlanDiffView w, bool collapsed = true) =>
        w with { DefaultCollapsed = collapsed };

    public static PlanDiffView Comments(this PlanDiffView w, List<DraftComment>? comments) =>
        w with { Comments = comments };

    public static PlanDiffView FilePath(this PlanDiffView w, string filePath) =>
        w with { FilePath = filePath };

    public static PlanDiffView OnLineClick(
        this PlanDiffView w,
        Func<Event<PlanDiffView, int>, ValueTask> handler
    ) => w with { OnLineClick = handler };

    public static PlanDiffView OnLineClick(this PlanDiffView w, Action<int> handler) =>
        w with
        {
            OnLineClick = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanDiffView OnAddComment(
        this PlanDiffView w,
        Func<Event<PlanDiffView, DraftComment>, ValueTask> handler
    ) => w with { OnAddComment = handler };

    public static PlanDiffView OnAddComment(this PlanDiffView w, Action<DraftComment> handler) =>
        w with
        {
            OnAddComment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanDiffView OnDeleteComment(
        this PlanDiffView w,
        Func<Event<PlanDiffView, DraftComment>, ValueTask> handler
    ) => w with { OnDeleteComment = handler };

    public static PlanDiffView OnDeleteComment(this PlanDiffView w, Action<DraftComment> handler) =>
        w with
        {
            OnDeleteComment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanDiffView OnUpdateComment(
        this PlanDiffView w,
        Func<Event<PlanDiffView, DraftComment>, ValueTask> handler
    ) => w with { OnUpdateComment = handler };

    public static PlanDiffView OnUpdateComment(this PlanDiffView w, Action<DraftComment> handler) =>
        w with
        {
            OnUpdateComment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanDiffView OnDirectEdit(
        this PlanDiffView w,
        Func<Event<PlanDiffView, DirectEditArgs>, ValueTask> handler
    ) => w with { OnDirectEdit = handler };

    public static PlanDiffView OnDirectEdit(this PlanDiffView w, Action<DirectEditArgs> handler) =>
        w with
        {
            OnDirectEdit = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };
}
