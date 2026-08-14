using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

/// <summary>
///     The read-only half: no <c>OnAnswersChange</c> subscriber, so every block renders as the
///     static blue callout regardless of whether it parses as the structured schema.
///     <para>
///         This is the regression guard for "an answered plan still reads correctly", and for the
///         legacy plain-text fence that shipped before the schema existed.
///     </para>
/// </summary>
[App(title: "Questions (Answered)", icon: Icons.CircleCheck, group: ["DraftMarkdown"])]
class QuestionsAnsweredApp : ViewBase
{
    private const string Markdown = """
        # Notification Delivery: Decisions Taken

        This plan has been through review. Nothing below is interactive — the host did not
        subscribe to `OnAnswersChange`, which is what keeps a plan readable in contexts that
        only display it.

        ## Answered

        Every question here carries an `answer`, including a multi-select whose answer is a
        list and a free-text answer that matches no option.

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
              - title: Per tenant
                value: per-tenant
            answer: per-session
          - id: launch-channels
            title: Which channels should ship in the first release?
            header: Channels
            multiple: true
            options:
              - title: In-app
                value: in-app
              - title: Email
                value: email
              - title: Push
                value: push
              - title: Webhook
                value: webhook
            answer:
              - in-app
              - email
          - id: service-name
            title: What should the service be called?
            header: Name
            answer: courier
        ```

        ## Skipped

        `answer: null` is not the same as an absent `answer`. It means the question was
        asked and deliberately handed back — "you decide".

        ```questions
        questions:
          - id: dead-letter
            title: How long should undeliverable messages be retained?
            header: Dead letter
            description: Left to the implementer.
            options:
              - title: 7 days
                value: 7d
              - title: 30 days
                value: 30d
            answer: null
        ```

        ## Legacy

        A fence whose body is not a YAML mapping with a `questions` key is the plain-text
        form that predates the schema. Existing revisions contain it, it is never rewritten,
        and it renders exactly as it always has.

        ```questions
        Should we support notification templates with variables?
        What is the retention policy for read notifications?
        Do we need delivery confirmation for critical notifications?
        ```
        """;

    public override object Build() =>
        Layout.Horizontal().Height(Size.Full()).RemoveParentPadding()
        | new DraftMarkdownWidget(Markdown)
            .Article()
            .Width(Size.Full())
            .Height(Size.Full());
}
