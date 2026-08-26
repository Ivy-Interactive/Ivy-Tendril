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
///         why a selection sticks and a skip announces itself.
///     </para>
///     <para>
///         The pinned card is the other half of the pattern, and the reason a plan this long stays
///         navigable: <see cref="QuestionAnswers.Read" /> turns the document into an index, and
///         <see cref="DraftMarkdownWidget.ScrollTo" /> takes you to whichever entry you click.
///         Answered entries strike through, so the card empties as the plan is settled.
///     </para>
///     <para>
///         Between them the four blocks cover the whole schema: a four-question block under the H1
///         (the cap, and the scope-level placement the spec asks for), every shape from the shape
///         table including multi-select over a fixed set, questions that arrive already answered
///         with a scalar and with a list, one with no <c>header</c>, an optional question, and a
///         documentation fence that must never become a picker.
///     </para>
/// </summary>
[App(title: "Questions", icon: Icons.CircleQuestionMark, group: ["DraftMarkdown"])]
class QuestionsApp : ViewBase
{
    private const string InitialMarkdown = """
        # Notification Delivery: Open Decisions

        ```questions
        questions:
          - id: delivery-scope
            title: How much of the pipeline is in scope for this plan?
            header: Scope
            description: |
              A block placed directly under the H1, which is where a question about the
              plan's overall scope belongs. Four questions is the cap, and they stack.
            other: false
            options:
              - title: Dispatch only
                description: The fan-out and the consumers. No settings UI.
                value: dispatch
                recommended: true
              - title: Dispatch and preferences
                description: Adds the per-channel settings page.
                value: dispatch-prefs
              - title: Everything
                description: Dispatch, preferences and the operator dashboard.
                value: everything
          - id: target-release
            title: Which release should this land in?
            header: Release
            description: Answered at the kickoff, so it arrives already filled in — this is
              what a revision looks like after UpdatePlan has folded a decision back in.
            other: false
            options:
              # Quoted, or YAML reads them as numbers. The renderer coerces either way, but a
              # sample should model the authoring the schema actually asks for.
              - title: "4.2"
                value: four-two
              - title: "4.3"
                value: four-three
                recommended: true
            answer: four-three
          - id: rollout-regions
            title: Which regions ship first?
            multiple: true
            other: false
            description: Multi-select over a fixed set — the third shape from the schema's
              table, and the only one the other blocks below do not cover.
            options:
              - title: EU
                value: eu
                recommended: true
              - title: US
                value: us
              - title: APAC
                value: apac
            answer:
              - eu
              - us
          - id: kickoff-notes
            title: Anything else the plan should account for?
            description: No `header` on this one, so it leads with its title and no eyebrow.
        ```

        Three further decisions are open before the delivery pipeline can be built. Each block
        below demonstrates a different shape from the schema.

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
        Each `header` becomes the eyebrow above its question. The second is `optional: true` —
        worth asking, but the plan is complete without it, so the index counts it as settled
        and only the first still wants a human.

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
            optional: true
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
            StartOffset = 1123,
            EndOffset = 1145,
            SelectedText = "Where the budget lives",
            Comment = "Ordinary prose annotates as usual.",
        },
        new()
        {
            // The point of the sample: an annotation aimed squarely at a question block renders
            // nothing. The picker is a form, and a <mark> spliced into it would fight React for
            // the DOM.
            Id = "question",
            StartOffset = 1468,
            EndOffset = 1506,
            SelectedText = "Should the retry budget be per-request",
            Comment = "Aimed at the question block — must not highlight.",
        },
        new()
        {
            // Guards the offset bookkeeping: a block's text is never highlighted but still counts
            // toward the offsets, so an annotation after one must not drift.
            Id = "after",
            StartOffset = 2259,
            EndOffset = 2276,
            SelectedText = "separate consumer",
            Comment = "After a block, so it proves the block's text still advances the offsets.",
        },
    ];

    public override object Build()
    {
        var markdown = UseState(InitialMarkdown);
        var annotations = UseState(DummyAnnotations);
        var scrollTo = UseState<QuestionScrollTarget?>(() => null);

        // Read straight off the document, so the index reflects every merge without a second copy
        // of the answer state living beside it.
        var questions = QuestionAnswers.Read(markdown.Value);

        // One rich text block rather than a stack of buttons: these are links in a list, and
        // buttons brought their own padding and hit targets to something that reads as prose.
        // The link's url carries the question id, which is what comes back to OnLinkClick.
        var index = Text.Rich();
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];

            // A blank line between entries: several of these titles wrap, and without the gap a
            // wrapped one runs straight into the next and the list reads as a paragraph.
            if (i > 0)
                index.LineBreak().LineBreak();

            // Struck through and muted once nothing is outstanding: it has an answer, or it is
            // optional and the plan does not wait on it. What stays live is what still wants a
            // human. Muting is what makes that stand out — the strike alone still reads as live.
            var settled = question.HasAnswer || question.IsOptional;

            index.Link(
                question.IsOptional ? $"{Label(question)} (Optional)" : Label(question),
                question.Id,
                strikeThrough: settled,
                color: settled ? Colors.Muted : null);
        }

        index.OnLinkClick(id => scrollTo.Set(new QuestionScrollTarget(
            id,
            // Any different value re-triggers the scroll, which is what makes clicking the same
            // entry twice work.
            (scrollTo.Value?.Token ?? 0) + 1)));

        var card = new Card(
            Layout.Vertical().Gap(1)
            | Text.Muted($"{questions.Count(q => q.HasAnswer || q.IsOptional)} of {questions.Count} settled. "
                         + "Click one to scroll its block into view.")
            | index
            | new Button("Reset").Outline().OnClick(() =>
            {
                markdown.Set(InitialMarkdown);
                annotations.Set(DummyAnnotations);
                scrollTo.Set((QuestionScrollTarget?)null);
            })
        ).Title("Open questions").Width(Size.Units(80));

        return Layout.Horizontal().Height(Size.Full()).RemoveParentPadding()
               | new DraftMarkdownWidget(markdown.Value)
                   .Article()
                   .OnAnswersChange(answer =>
                   {
                       // The merge is the host's job, and this is all of it. TryApply rather than
                       // Apply because an answer can outlive the document it was given against.
                       if (QuestionAnswers.TryApply(markdown.Value, answer, out var merged))
                           markdown.Set(merged);
                   })
                   .Annotations(annotations.Value)
                   .OnAnnotationsChange(a => annotations.Set(a))
                   .ScrollTo(scrollTo.Value)
                   .Width(Size.Full())
                   .Height(Size.Full())
                   .StickyContent(card);
    }

    /// <summary>The eyebrow when the question has one, else its title. Falls back to the id.</summary>
    private static string Label(QuestionSummary question) =>
        !string.IsNullOrWhiteSpace(question.Title) ? question.Title
        : !string.IsNullOrWhiteSpace(question.Header) ? question.Header
        : question.Id;

}
