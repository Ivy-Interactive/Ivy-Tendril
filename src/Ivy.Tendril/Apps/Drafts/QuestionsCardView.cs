using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Drafts;

/// <summary>
///     Sticky-sidebar card indexing every question in the plan, so a long revision stays navigable.
///     Clicking an entry scrolls its block into view.
///     <para>
///         An entry is struck through and muted once it carries an answer — what stays live is what
///         still wants a human. An <c>optional: true</c> question says so beside its title and stays
///         live until answered: optional means the plan does not wait on it, not that anybody has
///         dealt with it.
///     </para>
/// </summary>
public class QuestionsCardView(
    IReadOnlyList<QuestionSummary> questions,
    Action<string> onSelect) : ViewBase
{
    public override object Build()
    {
        // One element per entry rather than a single rich block: spacing is the layout's job, and a
        // title that wraps stays distinct from the next without a blank line between them.
        var inner = Layout.Vertical().Gap(2);

        foreach (var question in questions)
        {
            // Keyed, because sibling stateless views of the same type otherwise have nothing to
            // tell them apart and their link handlers do not survive the diff. Block index included:
            // ids are unique per the schema, but this renders whatever the user typed, and two
            // entries sharing a key is worse than two entries sharing an id.
            var entry = Text.Rich();
            entry.Key = $"{question.BlockIndex}:{question.Id}";

            inner |= entry
                .Link(
                    question.IsOptional ? $"{Label(question)} (Optional)" : Label(question),
                    question.Id,
                    strikeThrough: question.HasAnswer,
                    color: question.HasAnswer ? Colors.Muted : null)
                .OnLinkClick(onSelect);
        }

        var answered = questions.Count(q => q.HasAnswer);

        return new Card(
            Layout.Vertical().Gap(2)
            | Text.Muted($"{answered} of {questions.Count} answered")
            | inner
        ).Header("Questions").Width(Size.Px(280));
    }

    /// <summary>The question itself, falling back to its eyebrow and then to its id.</summary>
    private static string Label(QuestionSummary question) =>
        !string.IsNullOrWhiteSpace(question.Title) ? question.Title
        : !string.IsNullOrWhiteSpace(question.Header) ? question.Header
        : question.Id;
}
