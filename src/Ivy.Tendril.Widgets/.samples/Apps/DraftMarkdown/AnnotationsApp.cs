using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.PlanMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

[App(title: "Annotations", icon: Icons.Highlighter, group: ["DraftMarkdown"])]
class AnnotationsApp : ViewBase
{
    public override object Build()
    {
        var annotations = UseState(ImmutableList<MarkdownAnnotation>.Empty);

        var markdown = """
            # Feature Specification: User Notifications

            ## Overview
            The notification system delivers real-time updates to users across
            multiple channels including in-app, email, and push notifications.

            ## Requirements
            1. Notifications must be delivered within **5 seconds** of the triggering event
            2. Users can configure per-channel preferences in their settings
            3. Batch notifications should be grouped by category

            ## Architecture
            The system uses a fan-out pattern where each event is published to a
            central topic, and channel-specific consumers handle delivery:

            - **In-app**: WebSocket connection with fallback to polling
            - **Email**: Queued via SES with rate limiting
            - **Push**: Firebase Cloud Messaging for mobile devices

            ## Implementation

            ```typescript
            export async function refreshToken(token: string): Promise<string> {
              const response = await fetch('/api/auth/refresh', {
                method: 'POST',
                headers: { Authorization: `Bearer ${token}` },
              });
              const { accessToken } = await response.json();
              return accessToken;
            }
            ```

            ## Status

            | Channel | Status | Owner |
            |---------|--------|-------|
            | In-app | Done | @alice |
            | Email | In progress | @bob |
            | Push | Pending | TBD |

            ## Visual Reference

            ![Notification flow diagram](https://placehold.co/600x200/EEE/333?text=Notification+Flow+Diagram)

            ## Open Questions
            - Should we support notification templates with variables?
            - What is the retention policy for read notifications?
            - Do we need delivery confirmation for critical notifications?

            > **Note:** This spec is subject to review by the platform team
            > before implementation begins.

            ```questions
            Should the retry budget be per-request or per-session?
            What is the retention policy for read notifications?
            ```

            ## Appendix: Rollout Plan

            The rollout proceeds in four phases, each gated on the previous phase's
            success metrics before the next one begins. This section is intentionally
            long so the document overflows the widget's viewport and can be scrolled,
            which end-to-end tests rely on to verify that floating annotation UI stays
            anchored to the underlying text while scrolling.

            1. **Internal dogfood** — the platform team enables notifications for its
               own accounts and monitors delivery latency and error rates for one week.
            2. **Limited beta** — a small, opted-in cohort of external users receives
               notifications, with a kill switch ready in case of unexpected volume.
            3. **Staged rollout** — the feature is enabled for an increasing percentage
               of accounts (5%, 25%, 50%, 100%) over two weeks, with rollback criteria
               defined for each stage.
            4. **General availability** — notifications are enabled by default for all
               accounts, with the settings page exposed for per-channel opt-out.

            Each phase has a dedicated on-call rotation and a rollback runbook. Metrics
            reviewed at each gate include delivery latency (p50/p95/p99), delivery
            failure rate per channel, and unsubscribe rate. Any regression beyond the
            agreed thresholds pauses the rollout until root-caused.

            ### Risks and Mitigations

            - **Provider outages**: email and push both depend on third-party
              providers; the fan-out consumers retry with exponential backoff and
              fall back to in-app delivery when a channel is degraded.
            - **Notification fatigue**: batching and per-channel preferences reduce
              the chance that users disable notifications outright.
            - **Data consistency**: the topic is the single source of truth for
              delivery status, so a consumer crash cannot silently drop a
              notification — it re-reads from the last committed offset on restart.

            ### Appendix Glossary

            - **Fan-out**: publishing a single event to multiple independent
              consumers, one per delivery channel.
            - **Dead-letter queue**: where events land after exhausting their retry
              budget, for manual inspection.
            - **Kill switch**: an operator-controlled flag that disables a channel
              immediately, independent of the staged rollout percentage.
            """;

        object sidePanel;
        if (annotations.Value.Count > 0)
        {
            var items = annotations.Value.Select((a, i) =>
                (object)(Layout.Vertical().Gap(1)
                | Text.Muted($"\"{a.SelectedText}\"")
                | Text.Block(a.Comment)
                | new Button("Remove").Ghost().Destructive()
                    .OnClick(() => annotations.Set(annotations.Value.RemoveAt(i))))
            );

            sidePanel = Layout.Vertical().Gap(3).Width(Size.Units(80))
                        | Text.Block($"Annotations ({annotations.Value.Count})").Bold()
                        | items;
        }
        else
        {
            sidePanel = Layout.Vertical().Width(Size.Units(80))
                        | Text.Muted("Select text in the markdown to add annotations.");
        }

        return Layout.Horizontal().Height(Size.Full()).Gap(4)
               | new DraftMarkdownWidget(markdown)
                   .Article()
                   .Annotations(annotations.Value)
                   .OnAnnotationsChange(a => annotations.Set(a))
                   .Width(Size.Full())
                   .Height(Size.Full())
               | sidePanel;
    }
}
