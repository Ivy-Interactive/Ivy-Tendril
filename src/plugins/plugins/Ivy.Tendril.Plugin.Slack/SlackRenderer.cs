using System.Text;
using System.Text.Json.Nodes;
using Ivy.Plugins.Messaging;

namespace Ivy.Plugin.Slack;

internal static class SlackRenderer
{
    public static (JsonArray Blocks, string FallbackText) Render(MessageContent content)
    {
        var blocks = new JsonArray();
        var fallback = new StringBuilder();
        var inlineBuffer = new StringBuilder();

        RenderToBlocks(content, blocks, inlineBuffer, fallback);
        FlushInline(blocks, inlineBuffer);

        return (blocks, fallback.ToString().Trim());
    }

    private static void RenderToBlocks(
        MessageContent node,
        JsonArray blocks,
        StringBuilder inlineBuffer,
        StringBuilder fallback)
    {
        switch (node)
        {
            case TextNode t:
                var escaped = EscapeMrkdwn(t.Text);
                inlineBuffer.Append(escaped);
                fallback.Append(escaped);
                break;

            case BoldNode b:
                inlineBuffer.Append('*');
                fallback.Append('*');
                RenderToBlocks(b.Content, blocks, inlineBuffer, fallback);
                inlineBuffer.Append('*');
                fallback.Append('*');
                break;

            case ItalicNode i:
                inlineBuffer.Append('_');
                fallback.Append('_');
                RenderToBlocks(i.Content, blocks, inlineBuffer, fallback);
                inlineBuffer.Append('_');
                fallback.Append('_');
                break;

            case StrikethroughNode s:
                inlineBuffer.Append('~');
                fallback.Append('~');
                RenderToBlocks(s.Content, blocks, inlineBuffer, fallback);
                inlineBuffer.Append('~');
                fallback.Append('~');
                break;

            case CodeNode c:
                var code = $"`{c.Code}`";
                inlineBuffer.Append(code);
                fallback.Append(code);
                break;

            case CodeBlockNode cb:
                FlushInline(blocks, inlineBuffer);
                var codeBlock = $"```\n{cb.Code}\n```";
                blocks.Add(MrkdwnSection(codeBlock));
                fallback.Append(codeBlock);
                break;

            case LinkNode l:
                var link = l.Label is not null
                    ? $"<{l.Url}|{EscapeMrkdwn(l.Label)}>"
                    : $"<{l.Url}>";
                inlineBuffer.Append(link);
                fallback.Append(l.Label ?? l.Url);
                break;

            case ImageNode img:
                FlushInline(blocks, inlineBuffer);
                blocks.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["image_url"] = img.Url,
                    ["alt_text"] = img.AltText,
                });
                fallback.Append($"[{img.AltText}]");
                break;

            case LineBreakNode:
                inlineBuffer.Append('\n');
                fallback.Append('\n');
                break;

            case DividerNode:
                FlushInline(blocks, inlineBuffer);
                blocks.Add(new JsonObject { ["type"] = "divider" });
                break;

            case SectionNode section:
                FlushInline(blocks, inlineBuffer);
                var sectionInline = new StringBuilder();
                var sectionFallback = new StringBuilder();
                RenderToBlocks(section.Content, blocks, sectionInline, sectionFallback);

                var sectionBlock = MrkdwnSection(sectionInline.ToString());
                if (section.Accessory is not null)
                {
                    sectionBlock["accessory"] = new JsonObject
                    {
                        ["type"] = "image",
                        ["image_url"] = section.Accessory.Url,
                        ["alt_text"] = section.Accessory.AltText,
                    };
                }
                blocks.Add(sectionBlock);
                fallback.Append(sectionFallback);
                break;

            case SequenceNode seq:
                foreach (var child in seq.Children)
                    RenderToBlocks(child, blocks, inlineBuffer, fallback);
                break;
        }
    }

    private static void FlushInline(JsonArray blocks, StringBuilder inlineBuffer)
    {
        if (inlineBuffer.Length == 0) return;
        blocks.Add(MrkdwnSection(inlineBuffer.ToString()));
        inlineBuffer.Clear();
    }

    private static JsonObject MrkdwnSection(string text) => new()
    {
        ["type"] = "section",
        ["text"] = new JsonObject
        {
            ["type"] = "mrkdwn",
            ["text"] = text,
        },
    };

    private static string EscapeMrkdwn(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
