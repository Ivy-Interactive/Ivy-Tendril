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

        Option descriptions are full block markdown — the two below carry the call signature
        each one implies, which settles the question faster than another paragraph would. The
        fence is opened with four backticks so the snippets inside can use three.

        ````questions
        questions:
          - id: retry-scope
            title: Should the retry budget be per-request or per-session?
            header: Retry scope
            description: A **fixed** set — one of these three, no free text.
            other: false
            options:
              - title: Per request
                description: |
                  Each call gets its own budget. Simple, but a retry storm multiplies
                  across a session.

                  ```csharp
                  await client.SendAsync(request, new RetryBudget(maxAttempts: 3));
                  ```
                value: per-request
              - title: Per session
                description: |
                  One budget shared by every call in the session, so a storm is capped
                  no matter how many calls make it up.

                  ```csharp
                  using var session = client.OpenSession(new RetryBudget(maxAttempts: 3));
                  await session.SendAsync(request);
                  ```
                value: per-session
                recommended: true
              - title: Per tenant
                description: |
                  One budget for the whole tenant. Fairest under load, hardest to reason
                  about locally — the ceiling lives in config rather than at the call site:

                  | Setting | Scope | Default |
                  |---|---|---|
                  | `retry.maxAttempts` | tenant | 3 |
                  | `retry.window` | tenant | 30s |
                value: per-tenant
        ````

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

        Two questions with no options at all — the pure free-text shape. A block may hold up
        to four, and they stack, so both are answerable without hunting for the second one.
        Each `header` becomes the eyebrow above its question. The second carries `answer:
        null` — asked and deliberately skipped — which the picker says out loud instead of
        leaving it looking untouched.

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

    /// <summary>
    ///     Three annotations placed by hand so the interaction between annotations and question
    ///     blocks is visible without having to drag anything.
    ///     <para>
    ///         Offsets are into the widget's <em>rendered plain text</em>, not the markdown source,
    ///         which is why they are opaque numbers — and why editing the document above means
    ///         re-measuring them.
    ///     </para>
    /// </summary>
    private static readonly ImmutableList<MarkdownAnnotation> DummyAnnotations =
    [
        new()
        {
            Id = "prose",
            StartOffset = 274,
            EndOffset = 296,
            SelectedText = "Where the budget lives",
            Comment = "Ordinary prose annotates as usual.",
        },
        new()
        {
            // The point of the sample: an annotation aimed squarely at a question block renders
            // nothing. The picker is a form, and a <mark> spliced into it would fight React for
            // the DOM.
            Id = "question",
            StartOffset = 619,
            EndOffset = 657,
            SelectedText = "Should the retry budget be per-request",
            Comment = "Aimed at the question block — must not highlight.",
        },
        new()
        {
            // Guards the offset bookkeeping: a block's text is never highlighted but still counts
            // toward the offsets, so an annotation after one must not drift.
            Id = "after",
            StartOffset = 1410,
            EndOffset = 1427,
            SelectedText = "separate consumer",
            Comment = "After a block, so it proves the block's text still advances the offsets.",
        },
    ];

    public override object Build()
    {
        var markdown = UseState(InitialMarkdown);
        var answers = UseState(ImmutableList<QuestionAnswer>.Empty);
        var annotations = UseState(DummyAnnotations);

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
                    | Text.Block($"Annotations ({annotations.Value.Count})").Bold()
                    | Text.Muted("Seeded, one of them aimed at a question block. Only two are "
                                 + "highlighted: a question block is a form, and it never takes a "
                                 + "highlight — dragging across one raises no toolbar either.")
                    | new Button("Reset").Outline().OnClick(() =>
                    {
                        markdown.Set(InitialMarkdown);
                        answers.Set(ImmutableList<QuestionAnswer>.Empty);
                        annotations.Set(DummyAnnotations);
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
                   .Annotations(annotations.Value)
                   .OnAnnotationsChange(a => annotations.Set(a))
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
