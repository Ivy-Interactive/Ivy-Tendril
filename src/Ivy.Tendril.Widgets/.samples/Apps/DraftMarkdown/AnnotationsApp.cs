using System.Collections.Immutable;
using System.Text.Json;
using Ivy;
using Ivy.Widgets.DraftMarkdown;

namespace WidgetSamples;

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

            ## Open Questions
            - Should we support notification templates with variables?
            - What is the retention policy for read notifications?
            - Do we need delivery confirmation for critical notifications?

            > **Note:** This spec is subject to review by the platform team
            > before implementation begins.
            """;

        var annotationInfo = annotations.Value.Count > 0
            ? Layout.Vertical().Gap(1)
              | Text.Block($"Annotations: {annotations.Value.Count}").Bold()
              | new CodeBlock(JsonSerializer.Serialize(annotations.Value, new JsonSerializerOptions { WriteIndented = true }))
            : (object)Text.Muted("Select text in the markdown to add annotations.");

        return Layout.Vertical().Height(Size.Full()).Gap(2)
               | new DraftMarkdown(markdown)
                   .Article()
                   .Annotations(annotations.Value)
                   .OnAnnotationsChange(a => annotations.Set(a))
                   .Height(Size.Fraction(0.65f))
               | annotationInfo;
    }
}
