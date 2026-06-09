using Ivy;
using Ivy.Widgets.DraftMarkdown;

namespace Ivy.Tendril.Widgets.Samples;

[App(title: "Draft Markdown", icon: Icons.FileText, group: ["DraftMarkdown"])]
class DraftMarkdownApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var lastLink = UseState<string?>(null);

        var fixedPanel = Layout.Vertical().Gap(2).Width(Size.Px(260))
                         | Text.Block("Last Clicked Link").Bold()
                         | Text.P(lastLink.Value ?? "(none)");

        var markdown = """
            # Plan: Refactor Authentication Module

            ## Overview
            This plan restructures the auth middleware to support [OAuth 2.0](https://oauth.net/2/)
            flows and adds [token refresh logic](./token-refresh-plan.md).

            ## Steps
            1. Extract token validation into `src/auth/validate.ts`
            2. Add refresh endpoint (see [API spec](file://docs/api-spec.md))
            3. Update middleware pipeline

            ## Dependencies
            - Depends on [Plan #00042](./00042-api-gateway/plan.md)

            | Step | Status | Owner |
            |------|--------|-------|
            | Extract validation | Done | @alice |
            | Refresh endpoint | In progress | @bob |
            | Pipeline update | Pending | TBD |

            ## Code Example

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

            > **Note:** The refresh endpoint requires the legacy token format until
            > the migration in Plan #00043 is complete.
            """;

        return new DraftMarkdown(markdown)
            .Article()
            .DangerouslyAllowLocalFiles()
            .Height(Size.Full())
            .FixedContent(fixedPanel)
            .OnLinkClick(href =>
            {
                lastLink.Set(href);
                client.Toast($"Link clicked: {href}", "OnLinkClick").Info();
            });
    }
}
