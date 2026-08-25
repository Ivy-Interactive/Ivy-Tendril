using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

/// <summary>
///     Interactive `questions` blocks. Subscribing to <c>OnAnswersChange</c> is what turns the
///     read-only blue callout into a picker.
///     <para>
///         The widget never rewrites its own document: the event reports <c>{ QuestionId, Answer }</c>
///         and the host decides how and whether to persist it. This sample persists — it merges each
///         event straight back into the markdown with <see cref="QuestionAnswers.Apply" />, which is
///         why a selection sticks, a tab grows a check badge and a skip announces itself. The pinned
///         panel shows both halves of that loop: the raw event stream, and the block source it
///         produced.
///     </para>
/// </summary>
[App(title: "Questions", icon: Icons.CircleQuestionMark, group: ["DraftMarkdown"])]
class QuestionsApp : ViewBase
{
    private const string InitialMarkdown = """
        # Notification Delivery: Open Decisions

        Three decisions are still open before the delivery pipeline can be built. Each is a
        `questions` block, and each block demonstrates a different shape from the schema.

        ## Retry policy

        A burst of provider failures has to be absorbed somewhere. Where the budget lives
        decides whether one bad request can starve the rest of a session.

        ```questions
        questions:
          - id: retry-scope
            title: Should the retry budget be per-request or per-session?
            header: Retry scope
            description: A **fixed** set — one of these three, no free text.
            other: false
            options:
              - title: Per request
                description: Each call gets its own budget. Simple, but a retry storm
                  multiplies across a session.
                value: per-request
              - title: Per session
                description: One budget shared by every call in the session, so a storm
                  is capped no matter how many calls make it up.
                value: per-session
                recommended: true
              - title: Per tenant
                description: One budget for the whole tenant. Fairest under load, hardest
                  to reason about locally.
                value: per-tenant
        ```

        ## Launch channels

        Delivery is fanned out per channel, and each channel is a separate consumer. We do
        not have to ship them all at once.

        ```questions
        questions:
          - id: launch-channels
            title: Which channels should ship in the first release?
            header: Channels
            description: Multi-select, and you may add a channel that is not listed.
            multiple: true
            options:
              - title: In-app
                description: WebSocket with a polling fallback.
                value: in-app
              - title: Email
                description: Queued via SES with rate limiting.
                value: email
                recommended: true
              - title: Push
                description: Firebase Cloud Messaging for mobile.
                value: push
              - title: Webhook
                description: Outbound POST to a tenant-supplied URL.
                value: webhook
        ```

        ## Naming and ownership

        Two questions with no options at all — the pure free-text shape. Because there is
        more than one question in this block, they render as a tab strip. The second one
        carries `answer: null` — asked and deliberately skipped — which the picker says out
        loud instead of leaving it looking untouched.

        ```questions
        questions:
          - id: service-name
            title: What should the service be called?
            header: Name
            description: Something short enough to fit in a log prefix.
          - id: rollout-owner
            title: Who owns the rollout?
            header: Owner
            description: The person paged when delivery latency regresses.
            answer: null
        ```

        ## Not a question

        The block below is documentation, not a question — it is written inside a longer
        fence, so it renders as an ordinary code block and never becomes a picker.

        ````
        ```questions
        questions:
          - id: example
            title: This one is an example, not a live question.
        ```
        ````
        """;

    public override object Build()
    {
        var markdown = UseState(InitialMarkdown);
        var answers = UseState(ImmutableList<QuestionAnswer>.Empty);

        var stream = answers.Value.Count > 0
            ? (object)(Layout.Vertical().Gap(3)
               | answers.Value.Reverse().Select(answer =>
                   (object)(Layout.Vertical().Gap(1)
                   | Text.Block(answer.QuestionId).Bold()
                   | Text.Muted(Describe(answer)))))
            : Text.Muted("Pick an option, type an answer, or press Clear. Every change "
                         + "shows up here as a QuestionAnswer record.");

        var panel = Layout.Vertical().Gap(3).Width(Size.Units(80))
                    | Text.Block($"Answers received ({answers.Value.Count})").Bold()
                    | Text.Muted("Newest first. Each entry is one OnAnswersChange event.")
                    | stream
                    | Text.Block("Block source").Bold()
                    | Text.Muted("The fence QuestionAnswers.Apply last edited. Only the answer key "
                                 + "moves — comments, key order and the other blocks stay put.")
                    | Text.Code(SourceOf(markdown.Value, answers.Value.LastOrDefault()), Languages.Yaml)
                    | new Button("Reset").Outline().OnClick(() =>
                    {
                        markdown.Set(InitialMarkdown);
                        answers.Set(ImmutableList<QuestionAnswer>.Empty);
                    });

        return Layout.Horizontal().Height(Size.Full()).RemoveParentPadding()
               | new DraftMarkdownWidget(markdown.Value)
                   .Article()
                   .OnAnswersChange(answer =>
                   {
                       // The merge is the host's job, and this is all of it. TryApply rather than
                       // Apply because an answer can outlive the document it was given against.
                       if (QuestionAnswers.TryApply(markdown.Value, answer, out var merged))
                           markdown.Set(merged);

                       answers.Set(answers.Value.Add(answer));
                   })
                   .Width(Size.Full())
                   .Height(Size.Full())
                   .StickyContent(panel);
    }

    /// <summary>
    ///     The body of the block holding <paramref name="last" />'s question, or the first block
    ///     before anything has been answered. One block rather than the whole document, because the
    ///     point is to watch a single fence change.
    /// </summary>
    private static string SourceOf(string markdown, QuestionAnswer? last)
    {
        var blocks = QuestionAnswers.Scan(markdown);
        if (blocks.Count == 0)
            return "";

        var touched = last is null
            ? null
            : blocks.Cast<QuestionBlockSource?>()
                .FirstOrDefault(b => b!.Value.Body.Contains($"id: {last.QuestionId}", StringComparison.Ordinal));

        return (touched ?? blocks[0]).Body.TrimEnd();
    }

    /// <summary>The record's tri-state <c>Answer</c>, spelled out.</summary>
    private static string Describe(QuestionAnswer answer) => answer.Answer switch
    {
        null => "cleared — back to unanswered",
        { Count: 0 } => "skipped — answer: null",
        var entries => string.Join(", ", entries),
    };
}
