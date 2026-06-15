using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

[App(title: "Sticky Content", icon: Icons.PanelRight, group: ["DraftMarkdown"])]
class StickyContentApp : ViewBase
{
    public override object Build()
    {
        var markdown = """
            # Document with Fixed Sidebar

            ## Introduction
            The StickyContent slot renders a pinned panel to the right of the scrollable
            markdown body. It stays in place while the markdown scrolls, making it ideal
            for navigation, metadata, or action panels.

            ## How It Works
            The widget uses a flex layout with two siblings:
            - `.pmv-body` — scrollable markdown container
            - `.pmv-sticky` — pinned slot (does not scroll with content)

            Scroll down to see the sticky panel remain in place while the markdown
            body scrolls independently.

            ## Section One
            Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod
            tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam,
            quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo
            consequat.

            Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore
            eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident,
            sunt in culpa qui officia deserunt mollit anim id est laborum.

            ## Section Two
            Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium
            doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore
            veritatis et quasi architecto beatae vitae dicta sunt explicabo.

            Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit,
            sed quia consequuntur magni dolores eos qui ratione voluptatem sequi nesciunt.
            Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet, consectetur,
            adipisci velit.

            ## Section Three: Implementation

            ```csharp
            public class StickyPanelDemo
            {
                public object Build()
                {
                    var sidebar = new Card()
                        .Header(Text.H4("Navigation"))
                        .Content(BuildTableOfContents());

                    return new DraftMarkdown(markdown)
                        .Article()
                        .StickyContent(sidebar);
                }
            }
            ```

            The implementation is straightforward: wrap your navigation or metadata
            in any Ivy widget and pass it to `.StickyContent()`.

            ## Section Four: Use Cases

            Common patterns for the sticky slot include:

            1. **Table of contents** — navigate long documents without losing position
            2. **Metadata panels** — show document status, author, last modified
            3. **Action buttons** — approve, reject, comment actions always accessible
            4. **Progress indicators** — reading progress or completion status
            5. **Related links** — cross-references to other documents or plans

            Each of these benefits from remaining visible regardless of scroll position
            in the main content area.

            ## Section Five: Configuration

            | Property | Type | Description |
            |----------|------|-------------|
            | Content | string | Markdown source text |
            | Article | bool | Article-grade typography |
            | StickyContent | object | Pinned sidebar widget |
            | Annotations | list | Text annotation highlights |
            | OnLinkClick | event | Fired when a link is clicked |

            ## Section Six
            At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis
            praesentium voluptatum deleniti atque corrupti quos dolores et quas molestias
            excepturi sint occaecati cupiditate non provident, similique sunt in culpa qui
            officia deserunt mollitia animi, id est laborum et dolorum fuga.

            Et harum quidem rerum facilis est et expedita distinctio. Nam libero tempore,
            cum soluta nobis est eligendi optio cumque nihil impedit quo minus id quod
            maxime placeat facere possimus, omnis voluptas assumenda est, omnis dolor
            repellendus.

            ## Section Seven
            Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus
            saepe eveniet ut et voluptates repudiandae sint et molestiae non recusandae.
            Itaque earum rerum hic tenetur a sapiente delectus, ut aut reiciendis
            voluptatibus maiores alias consequatur aut perferendis doloribus asperiores
            repellat.

            ## Section Eight
            Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse quam
            nihil molestiae consequatur, vel illum qui dolorem eum fugiat quo voluptas
            nulla pariatur? At vero eos et accusamus et iusto odio dignissimos ducimus
            qui blanditiis praesentium voluptatum deleniti.

            ## Conclusion
            The fixed content slot is useful for table-of-contents navigation, metadata
            displays, or action buttons that should remain accessible while scrolling
            through long documents. This example demonstrates that the sidebar stays
            pinned while you scroll through all the sections above.
            """;

        var sidebar = new Card()
            .Header(Text.H4("Table of Contents"))
            .Content(
                Layout.Vertical().Gap(2)
                | Text.Muted("Introduction")
                | Text.Muted("How It Works")
                | Text.Muted("Section One")
                | Text.Muted("Section Two")
                | Text.Muted("Section Three: Implementation")
                | Text.Muted("Section Four: Use Cases")
                | Text.Muted("Section Five: Configuration")
                | Text.Muted("Section Six")
                | Text.Muted("Section Seven")
                | Text.Muted("Section Eight")
                | Text.Muted("Conclusion"))
            .Width(Size.Units(72));

        return Layout.Horizontal().Height(Size.Full()).RemoveParentPadding()
               | new DraftMarkdownWidget(markdown)
                   .Article()
                   .Width(Size.Full())
                   .Height(Size.Full())
                   .StickyContent(sidebar);
    }
}
