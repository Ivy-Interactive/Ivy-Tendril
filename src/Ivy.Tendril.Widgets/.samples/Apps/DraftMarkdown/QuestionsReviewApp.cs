using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.PlanMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

/// <summary>
///     The read-only half: a settled plan, shown rather than worked through.
///     <para>
///         Not subscribing to <c>OnAnswersChange</c> is the whole switch. Every <c>questions</c>
///         block then renders what was asked and what was decided — the chosen option's title, or
///         the user's own words — with no radios, no free-text inputs and no Clear. This is what
///         the Review stage wants, and anywhere else in Tendril that shows a plan the reader is no
///         longer editing.
///     </para>
///     <para>
///         An unanswered question still appears, and says which kind it is. A required one was
///         resolved by the executing agent (taking the <c>recommended</c> option where there was
///         one); an <c>optional: true</c> one never needed an answer at all. Both are worth seeing
///         in review — they are decisions nobody explicitly made.
///     </para>
/// </summary>
[App(title: "Questions (Review)", icon: Icons.CircleCheck, group: ["DraftMarkdown"])]
class QuestionsReviewApp : ViewBase
{
    private const string SettledPlan = """
        # Notification Delivery: Decisions

        The plan as it stands after review. Every block below is the same schema the
        picker renders — the difference is that this host does not subscribe to
        `OnAnswersChange`, so the blocks present answers instead of controls.

        ## Scope

        ````questions
        questions:
          - id: delivery-scope
            title: How much of the pipeline is in scope?
            header: Scope
            description: Answered in the kickoff, so the answer shows as the option's title
              rather than the `dispatch` slug the YAML actually carries.
            other: false
            options:
              - title: Dispatch only
                description: |
                  The fan-out and the consumers. No settings UI.

                  ```csharp
                  services.AddDelivery(o => o.Channels = Channels.InApp | Channels.Email);
                  ```
                value: dispatch
                recommended: true
              - title: Dispatch and preferences
                description: Adds the per-channel settings page.
                value: dispatch-prefs
            answer: dispatch
          - id: rollout-regions
            title: Which regions ship first?
            header: Regions
            description: A multi-select answer lists every value it carries.
            multiple: true
            other: false
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
        ````

        ## Naming

        ```questions
        questions:
          - id: service-name
            title: What should the service be called?
            header: Name
            description: A pure free-text question the user answered. There is no option to
              look up, so the answer is shown exactly as it was typed.
            answer: Dispatch
          - id: naming-rationale
            title: Why that name?
            header: Rationale
            description: Free text is not always a word — a sentence has to read as prose
              rather than as a chip.
            answer: It is short enough for a log prefix and does not collide with the
              existing Delivery namespace, which already means something else in billing.
          - id: backoff-curve
            title: What backoff curve should sit under the budget?
            header: Backoff
            description: |
              Asked with options and `other` left at its default, and the user took neither
              option — they typed their own. The answer matches no `value`, so it is their
              words rather than an option title.
            options:
              - title: Exponential
                description: Doubling, with jitter.
                value: exponential
                recommended: true
              - title: Fixed interval
                description: Every 30s, up to the budget.
                value: fixed
            answer: Exponential, but capped at 2 minutes between attempts
          - id: rollout-owner
            title: Who owns the rollout?
            header: Owner
            description: Optional, and never answered. The plan did not wait on it.
            optional: true
          - id: metric-prefix
            title: What should the exhaustion metric be called?
            header: Metric
            description: Required, and never answered — so the executing agent decided. Worth
              seeing in review precisely because no human chose it.
        ```

        ## Not a question

        The block below is documentation. It stays a code block here exactly as it does in
        the picker.

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
        var questions = QuestionAnswers.Read(SettledPlan);
        var answered = questions.Count(q => q.HasAnswer);

        var panel = Layout.Vertical().Gap(3).Width(Size.Units(80))
                    | Text.Block("Read-only").Bold()
                    | Text.Muted($"{answered} of {questions.Count} questions carry an answer. The "
                                 + "rest were decided by the agent, or were optional.")
                    | Text.Muted("There is no OnAnswersChange handler on this widget — that is the "
                                 + "only difference from the Questions sample.");

        return Layout.Horizontal().Height(Size.Full()).RemoveParentPadding()
               | new DraftMarkdownWidget(SettledPlan)
                   .Article()
                   .Width(Size.Full())
                   .Height(Size.Full())
                   .StickyContent(panel);
    }
}
