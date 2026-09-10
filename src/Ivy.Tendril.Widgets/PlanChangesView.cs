namespace Ivy.Tendril.Widgets;

public record ChangedFileDto(string FilePath, string Status, string Diff, int Additions, int Deletions);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "PlanChangesView",
    GlobalName = "IvyTendrilWidgets"
)]
public record PlanChangesView : WidgetBase<PlanChangesView>
{
    [Prop] public List<ChangedFileDto> Files { get; init; } = [];

    [Prop] public List<DraftComment>? Comments { get; init; }

    [Prop] public string? CurrentAuthor { get; init; }

    [Prop] public DiffViewType ViewType { get; init; } = DiffViewType.Unified;

    [Prop] public bool WordWrap { get; init; } = true;

    [Prop] public int TreeWidth { get; init; } = 256;

    [Event] public Func<Event<PlanChangesView, DraftComment>, ValueTask>? OnAddComment { get; init; }

    [Event] public Func<Event<PlanChangesView, DraftComment>, ValueTask>? OnDeleteComment { get; init; }

    [Event] public Func<Event<PlanChangesView, DraftComment>, ValueTask>? OnUpdateComment { get; init; }

    [Event] public Func<Event<PlanChangesView, DirectEditArgs>, ValueTask>? OnDirectEdit { get; init; }
}

public static class PlanChangesViewExtensions
{
    public static PlanChangesView Key(this PlanChangesView w, string key)
    {
        w.Key = key;
        return w;
    }

    public static PlanChangesView Files(this PlanChangesView w, List<ChangedFileDto> files) =>
        w with { Files = files };

    public static PlanChangesView Comments(this PlanChangesView w, List<DraftComment>? comments) =>
        w with { Comments = comments };

    public static PlanChangesView CurrentAuthor(this PlanChangesView w, string? currentAuthor) =>
        w with { CurrentAuthor = currentAuthor };

    public static PlanChangesView ViewType(this PlanChangesView w, DiffViewType type) =>
        w with { ViewType = type };

    public static PlanChangesView WordWrap(this PlanChangesView w, bool wrap = true) =>
        w with { WordWrap = wrap };

    public static PlanChangesView TreeWidth(this PlanChangesView w, int width) =>
        w with { TreeWidth = width };

    public static PlanChangesView OnAddComment(
        this PlanChangesView w,
        Func<Event<PlanChangesView, DraftComment>, ValueTask> handler
    ) => w with { OnAddComment = handler };

    public static PlanChangesView OnAddComment(this PlanChangesView w, Action<DraftComment> handler) =>
        w with
        {
            OnAddComment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanChangesView OnDeleteComment(
        this PlanChangesView w,
        Func<Event<PlanChangesView, DraftComment>, ValueTask> handler
    ) => w with { OnDeleteComment = handler };

    public static PlanChangesView OnDeleteComment(this PlanChangesView w, Action<DraftComment> handler) =>
        w with
        {
            OnDeleteComment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanChangesView OnUpdateComment(
        this PlanChangesView w,
        Func<Event<PlanChangesView, DraftComment>, ValueTask> handler
    ) => w with { OnUpdateComment = handler };

    public static PlanChangesView OnUpdateComment(this PlanChangesView w, Action<DraftComment> handler) =>
        w with
        {
            OnUpdateComment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static PlanChangesView OnDirectEdit(
        this PlanChangesView w,
        Func<Event<PlanChangesView, DirectEditArgs>, ValueTask> handler
    ) => w with { OnDirectEdit = handler };

    public static PlanChangesView OnDirectEdit(this PlanChangesView w, Action<DirectEditArgs> handler) =>
        w with
        {
            OnDirectEdit = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };
}
