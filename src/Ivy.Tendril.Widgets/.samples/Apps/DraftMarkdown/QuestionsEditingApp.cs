using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

/// <summary>
///     What the picker does when the markdown behind it is edited by hand — which is what the plan
///     view allows, so every shape here is reachable in the product.
///     <para>
///         Type in the editor on the left and the rendering follows. The index above it is rebuilt
///         from the same text on every keystroke, so it is a live read of what the document actually
///         says rather than a cache that can disagree with it.
///     </para>
///     <para>
///         Nothing here throws. A block whose YAML does not parse falls back to showing its own text
///         — the widget's job is to display a plan, never to refuse to. The seeded document opens
///         with one healthy block and five broken ones, each labelled with what is wrong.
///     </para>
/// </summary>
[App(title: "Questions (Editing)", icon: Icons.PenLine, group: ["DraftMarkdown"])]
class QuestionsEditingApp : ViewBase
{
    private const string StartingMarkdown = """
        # Editing a plan by hand

        Everything below is one markdown document. Break it however you like — the
        rendering and the index both follow whatever you type.

        ## A healthy block

        ```questions
        questions:
          - id: retry-scope
            title: Should the retry budget be per-request or per-session?
            header: Retry scope
            other: false
            options:
              - title: Per request
                value: per-request
              - title: Per session
                value: per-session
                recommended: true
            answer: per-session
        ```

        ## Broken YAML

        An unclosed bracket. There is nothing to render as a picker, so the block shows
        its own text and the index simply does not list it.

        ```questions
        questions:
          - id: broken
            title: Which one?
            options: [unclosed
        ```

        ## Not the schema at all

        A fence whose body is prose rather than a `questions` mapping. This is the
        pre-schema form, and it still reads as what it is.

        ```questions
        Should we support notification templates? Nobody has decided yet.
        ```

        ## A question with no id

        An answer is addressed by `id` alone, so a block missing one is unsafe to answer
        anywhere in it. The whole block falls back to text, and the index lists none of
        it — the index and the rendering always agree on what is answerable.

        ```questions
        questions:
          - title: I have no id and cannot be answered.
          - id: has-id
            title: I do have one, so I still render.
        ```

        ## Two questions sharing an id

        The schema requires ids to be unique across a revision; hand-editing can break
        that. An answer could not say which question it meant, so this block falls back to
        text too — and `write-revision` rejects it rather than letting it reach a plan.

        ```questions
        questions:
          - id: same
            title: First one with this id.
          - id: same
            title: Second one with this id.
        ```

        ## An unterminated fence

        The fence below is never closed, so it runs to the end of the document. That is
        CommonMark, and the parser follows it rather than guessing.

        ```questions
        questions:
          - id: unterminated
            title: Everything after me is inside this fence.
        """;

    public override object Build()
    {
        var markdown = UseState(StartingMarkdown);
        var scrollTo = UseState<QuestionScrollTarget?>(() => null);

        // Rebuilt from the text on every render. Tolerant by design: a block that does not parse
        // contributes nothing rather than throwing, so a half-typed document still renders.
        var questions = QuestionAnswers.Read(markdown.Value);

        var index = Layout.Vertical().Gap(2);
        if (questions.Count == 0)
        {
            index |= Text.Muted("No readable questions in this document.");
        }
        else
        {
            foreach (var question in questions)
            {
                var entry = Text.Rich();
                // Block index included: a hand-edited document can repeat an id, and two entries
                // sharing a key is worse than two sharing an id.
                entry.Key = $"{question.BlockIndex}:{question.Id}";

                index |= entry
                    .Link(
                        question.IsOptional ? $"{Label(question)} (Optional)" : Label(question),
                        question.Id,
                        strikeThrough: question.HasAnswer,
                        color: question.HasAnswer ? Colors.Muted : null)
                    .OnLinkClick(id => scrollTo.Set(new QuestionScrollTarget(
                        id, (scrollTo.Value?.Token ?? 0) + 1)));
            }
        }

        var editor = Layout.Vertical().Gap(2).Width(Size.Units(120)).Height(Size.Full())
                     | (new Card(
                            Layout.Vertical().Gap(2)
                            | Text.Muted($"{questions.Count} readable, "
                                         + $"{questions.Count(q => q.HasAnswer)} answered")
                            | index
                            | new Button("Restore").Outline().OnClick(() => markdown.Set(StartingMarkdown))
                        ).Title("Questions"))
                     | markdown.ToCodeInput()
                         .Language(Languages.Markdown)
                         .Width(Size.Full())
                         .Height(Size.Full());

        return Layout.Horizontal().Height(Size.Full()).RemoveParentPadding()
               | editor
               | new DraftMarkdownWidget(markdown.Value)
                   .Article()
                   .OnAnswersChange(answer =>
                   {
                       // TryApply rather than Apply: the document may not contain that question any
                       // more by the time the answer arrives, which is ordinary while typing.
                       if (QuestionAnswers.TryApply(markdown.Value, answer, out var merged))
                           markdown.Set(merged);
                   })
                   .ScrollTo(scrollTo.Value)
                   .Width(Size.Full())
                   .Height(Size.Full());
    }

    private static string Label(QuestionSummary question) =>
        !string.IsNullOrWhiteSpace(question.Title) ? question.Title
        : !string.IsNullOrWhiteSpace(question.Header) ? question.Header
        : question.Id;
}
