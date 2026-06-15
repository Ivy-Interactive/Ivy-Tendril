using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

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
