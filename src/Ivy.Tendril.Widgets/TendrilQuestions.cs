namespace Ivy.Tendril.Widgets;

/// <param name="Values">
///     The question's whole answer: option values and any typed text, in selection order. Empty
///     when the question was cleared.
/// </param>
public record QuestionAnswerDto(string QuestionId, string[] Values);

/// <param name="Summary">The answers as a markdown list, for a host that records them as a message.</param>
public record QuestionsSubmissionDto(Dictionary<string, string[]> Answers, string Summary);

/// <summary>
///     A <c>questions</c> block (the YAML of a plan's fenced questions section) as a form: one
///     card per option, a free-text field where the schema allows one, and either live answer
///     events or a Submit button. The same widget presents settled answers when read-only. Used by
///     the chat for question blocks in assistant messages and reusable by the plan views.
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilQuestions",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilQuestions : WidgetBase<TendrilQuestions>
{
    [Prop] public string Content { get; init; } = "";
    [Prop] public bool ReadOnly { get; init; }
    [Prop] public bool ShowSubmit { get; init; }
    [Prop] public string? SubmitLabel { get; init; }

    [Event] public EventHandler<Event<TendrilQuestions, QuestionAnswerDto>>? OnAnswer { get; init; }
    [Event] public EventHandler<Event<TendrilQuestions, QuestionsSubmissionDto>>? OnSubmit { get; init; }
}

public static class TendrilQuestionsExtensions
{
    public static TendrilQuestions Content(this TendrilQuestions w, string content) =>
        w with { Content = content };

    public static TendrilQuestions ReadOnly(this TendrilQuestions w, bool readOnly = true) =>
        w with { ReadOnly = readOnly };

    public static TendrilQuestions ShowSubmit(this TendrilQuestions w, bool showSubmit = true) =>
        w with { ShowSubmit = showSubmit };

    public static TendrilQuestions SubmitLabel(this TendrilQuestions w, string? label) =>
        w with { SubmitLabel = label };

    public static TendrilQuestions OnAnswer(this TendrilQuestions w, Action<QuestionAnswerDto> handler) =>
        w with { OnAnswer = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static TendrilQuestions OnSubmit(this TendrilQuestions w, Action<QuestionsSubmissionDto> handler) =>
        w with { OnSubmit = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };
}
