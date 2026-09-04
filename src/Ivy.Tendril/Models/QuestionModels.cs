using System.Collections;
using System.Globalization;
using YamlDotNet.Serialization;

namespace Ivy.Tendril.Models;

/// <summary>
///     Whether a question carries an answer. Two states only: <c>answer</c> is absent or it is a
///     value, never null. A question that need not be answered says so with <c>Optional</c> when it
///     is written, rather than being marked after the fact.
/// </summary>
public enum AnswerState
{
    /// <summary>No <c>answer</c> key. Carry the question forward unchanged.</summary>
    Unanswered,

    /// <summary><c>answer</c> present with a value.</summary>
    Answered
}

/// <summary>
///     Body of a fenced <c>questions</c> block in plan revision markdown — the machine-readable
///     way a planning agent asks the user something it cannot settle by research.
///     <para>
///         The normative spec lives in <c>Prompts/Plans.md</c> (<c>## Question Blocks</c>), which is
///         injected into every promptware firmware. That document and this model must change
///         together. Blocks are extracted by <see cref="Helpers.QuestionBlockParser" /> and checked
///         by <c>QuestionValidationService</c>.
///     </para>
/// </summary>
public record QuestionsBlock
{
    /// <summary>1-4 questions. Deserialized from the <c>questions</c> key.</summary>
    public List<PlanQuestion> Questions { get; init; } = [];
}

/// <summary>A single question inside a <see cref="QuestionsBlock" />.</summary>
public record PlanQuestion
{
    /// <summary>
    ///     Stable handle for this question. Required, and unique across the whole revision rather
    ///     than just its own block: the UI reports an answer as an id and a value, with no block or
    ///     position alongside it, so a collision anywhere in the document makes an answer ambiguous.
    ///     Uniqueness is therefore checked by <c>QuestionValidationService</c>, which sees every block.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>The question itself. Required.</summary>
    public string Title { get; init; } = "";

    /// <summary>
    ///     Short label (max 12 chars) shown as an eyebrow above <see cref="Title" />. Optional —
    ///     a question without one simply leads with its title.
    /// </summary>
    public string? Header { get; init; }

    /// <summary>Markdown context shown under the question.</summary>
    public string? Description { get; init; }

    /// <summary>True when several options may be selected. Answers are then always a list.</summary>
    public bool Multiple { get; init; }

    /// <summary>Whether the user may type a value of their own. Defaults to true, per the schema.</summary>
    public bool Other { get; init; } = true;

    /// <summary>
    ///     Whether the plan is complete without an answer. Worth asking, not worth blocking on —
    ///     the UI treats an unanswered optional question as settled, so what remains outstanding is
    ///     what still wants a human.
    /// </summary>
    public bool Optional { get; init; }

    /// <summary>2-4 options, or null for a pure free-text question.</summary>
    public List<QuestionOption>? Options { get; init; }

    /// <summary>
    ///     The raw <c>answer</c> node: a scalar, a list, or null. Heterogeneous by design, because
    ///     <see cref="Other" /> lets an entry be either an option <see cref="QuestionOption.Value" />
    ///     or the user's own text.
    /// </summary>
    [YamlMember(Alias = "answer")]
    public object? RawAnswer { get; init; }

    /// <summary>
    ///     Whether the <c>answer</c> key was present in the source YAML. Set by the parser from the
    ///     raw YAML node: YamlDotNet cannot distinguish an absent key from an explicit null on a
    ///     plain property, and the two mean different things here.
    /// </summary>
    [YamlIgnore]
    public bool AnswerPresent { get; init; }

    /// <summary>
    ///     Answered when the key is present with a value. A present-but-null <c>answer</c> is not a
    ///     state the schema has — <c>QuestionValidationService</c> rejects it — so it reads as
    ///     unanswered here rather than inventing a third meaning for it.
    /// </summary>
    [YamlIgnore]
    public AnswerState AnswerState =>
        AnswerPresent && RawAnswer is not null ? AnswerState.Answered : AnswerState.Unanswered;

    /// <summary>Whether <c>answer</c> was written as a YAML sequence rather than a scalar.</summary>
    [YamlIgnore]
    public bool AnswerIsList => RawAnswer is IList;

    /// <summary>
    ///     The answer flattened to strings — empty when unanswered. An entry that equals
    ///     an option's <see cref="QuestionOption.Value" /> is that option; anything else is the
    ///     user's own text.
    /// </summary>
    [YamlIgnore]
    public IReadOnlyList<string> AnswerValues
    {
        get
        {
            switch (RawAnswer)
            {
                case null:
                    return [];
                case IList list:
                    var values = new List<string>(list.Count);
                    foreach (var item in list)
                    {
                        if (item is not null)
                            values.Add(Stringify(item));
                    }

                    return values;
                default:
                    return [Stringify(RawAnswer)];
            }
        }
    }

    private static string Stringify(object value) =>
        value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}

/// <summary>One selectable option of a <see cref="PlanQuestion" />.</summary>
public record QuestionOption
{
    /// <summary>1-5 words. Required.</summary>
    public string Title { get; init; } = "";

    /// <summary>Markdown body expanding on this option.</summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Stable slug referenced by <see cref="PlanQuestion.RawAnswer" />. Required, and constrained
    ///     to <c>^[a-z0-9][a-z0-9-]*$</c> so an option value can never be mistaken for free prose.
    /// </summary>
    public string Value { get; init; } = "";

    /// <summary>The option the asking agent would pick. At most one per question.</summary>
    public bool Recommended { get; init; }
}
