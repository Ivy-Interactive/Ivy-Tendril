using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Views;

public class LinkListView(List<Link> links, Action<string> onLinkClick) : ViewBase
{
    public override object Build()
    {
        if (links.Count == 0)
            return Text.Block("");

        var elements = new List<object>();

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            elements.Add(
                new Button()
                    .Ghost()
                    .Small()
                    .Title($"[{link.Title}]")
                    .OnClick(() => onLinkClick(link.Href))
            );

            if (i < links.Count - 1)
                elements.Add(Text.Block(", "));
        }

        return Layout.Horizontal().Gap(1).AlignContent(Align.Center)
               | elements;
    }
}
