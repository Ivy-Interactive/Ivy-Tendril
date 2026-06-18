using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Views;

public class LinkListView(List<Link> links, Action<string> onLinkClick) : ViewBase
{
    public override object Build()
    {
        if (links.Count == 0)
            return Text.Block("");

        var builder = Text.Rich().OnLinkClick(onLinkClick);
        
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            builder = builder.Link($"#{link.Title}", link.Href);
            
            if (i < links.Count - 1)
                builder = builder.Run(", ");
        }

        return builder;
    }
}
