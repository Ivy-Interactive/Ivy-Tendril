using System.Text;
using System.Text.Json;

namespace Ivy.Tendril.Services.IssueTrackers.Providers.Jira;

public static class AdfToMarkdownConverter
{
    public static string Convert(string? adfOrText)
    {
        if (string.IsNullOrWhiteSpace(adfOrText))
            return "";

        var trimmed = adfOrText.Trim();
        if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}"))
            return adfOrText;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "doc")
            {
                var sb = new StringBuilder();
                RenderNode(root, sb, 0);
                return sb.ToString().Trim();
            }
        }
        catch
        {
            // Fall back to original text if JSON parsing fails
        }

        return adfOrText;
    }

    private static void RenderNode(JsonElement node, StringBuilder sb, int listDepth, int? orderedIndex = null)
    {
        if (!node.TryGetProperty("type", out var typeProp)) return;
        var nodeType = typeProp.GetString();

        switch (nodeType)
        {
            case "doc":
                if (node.TryGetProperty("content", out var docContent))
                {
                    foreach (var child in docContent.EnumerateArray())
                    {
                        RenderNode(child, sb, listDepth);
                        sb.AppendLine();
                    }
                }
                break;

            case "paragraph":
                if (node.TryGetProperty("content", out var pContent))
                {
                    foreach (var child in pContent.EnumerateArray())
                    {
                        RenderInline(child, sb);
                    }
                }
                sb.AppendLine();
                break;

            case "heading":
                var level = 1;
                if (node.TryGetProperty("attrs", out var hAttrs) && hAttrs.TryGetProperty("level", out var lProp))
                {
                    level = Math.Clamp(lProp.GetInt32(), 1, 6);
                }
                sb.Append(new string('#', level)).Append(' ');
                if (node.TryGetProperty("content", out var hContent))
                {
                    foreach (var child in hContent.EnumerateArray())
                    {
                        RenderInline(child, sb);
                    }
                }
                sb.AppendLine().AppendLine();
                break;

            case "bulletList":
                if (node.TryGetProperty("content", out var bContent))
                {
                    foreach (var item in bContent.EnumerateArray())
                    {
                        RenderNode(item, sb, listDepth + 1, orderedIndex: null);
                    }
                }
                sb.AppendLine();
                break;

            case "orderedList":
                if (node.TryGetProperty("content", out var oContent))
                {
                    var idx = 1;
                    foreach (var item in oContent.EnumerateArray())
                    {
                        RenderNode(item, sb, listDepth + 1, orderedIndex: idx++);
                    }
                }
                sb.AppendLine();
                break;

            case "listItem":
                var indent = new string(' ', Math.Max(0, (listDepth - 1) * 2));
                var prefix = orderedIndex.HasValue ? $"{orderedIndex.Value}. " : "- ";
                sb.Append(indent).Append(prefix);

                if (node.TryGetProperty("content", out var liContent))
                {
                    var isFirst = true;
                    foreach (var child in liContent.EnumerateArray())
                    {
                        if (!isFirst) sb.Append(' ');
                        if (child.TryGetProperty("type", out var cType) && cType.GetString() == "paragraph")
                        {
                            if (child.TryGetProperty("content", out var nestedP))
                            {
                                foreach (var inline in nestedP.EnumerateArray())
                                {
                                    RenderInline(inline, sb);
                                }
                            }
                        }
                        else
                        {
                            RenderNode(child, sb, listDepth);
                        }
                        isFirst = false;
                    }
                }
                sb.AppendLine();
                break;

            case "codeBlock":
                var lang = "";
                if (node.TryGetProperty("attrs", out var cbAttrs) && cbAttrs.TryGetProperty("language", out var langProp))
                {
                    lang = langProp.GetString() ?? "";
                }
                sb.Append("```").AppendLine(lang);
                if (node.TryGetProperty("content", out var cbContent))
                {
                    foreach (var child in cbContent.EnumerateArray())
                    {
                        if (child.TryGetProperty("text", out var text))
                        {
                            sb.Append(text.GetString());
                        }
                    }
                }
                sb.AppendLine().AppendLine("```").AppendLine();
                break;

            case "blockquote":
                sb.Append("> ");
                if (node.TryGetProperty("content", out var bqContent))
                {
                    foreach (var child in bqContent.EnumerateArray())
                    {
                        RenderNode(child, sb, listDepth);
                    }
                }
                sb.AppendLine();
                break;

            case "rule":
                sb.AppendLine("---").AppendLine();
                break;

            default:
                if (node.TryGetProperty("content", out var defaultContent))
                {
                    foreach (var child in defaultContent.EnumerateArray())
                    {
                        RenderNode(child, sb, listDepth);
                    }
                }
                break;
        }
    }

    private static void RenderInline(JsonElement node, StringBuilder sb)
    {
        if (!node.TryGetProperty("type", out var typeProp)) return;
        var type = typeProp.GetString();

        if (type == "text")
        {
            var text = node.TryGetProperty("text", out var tProp) ? tProp.GetString() ?? "" : "";

            if (node.TryGetProperty("marks", out var marks))
            {
                var isBold = false;
                var isItalic = false;
                var isStrike = false;
                var isCode = false;
                string? linkHref = null;

                foreach (var mark in marks.EnumerateArray())
                {
                    if (mark.TryGetProperty("type", out var mType))
                    {
                        var m = mType.GetString();
                        if (m == "strong") isBold = true;
                        else if (m == "em") isItalic = true;
                        else if (m == "strike") isStrike = true;
                        else if (m == "code") isCode = true;
                        else if (m == "link" && mark.TryGetProperty("attrs", out var lAttrs) && lAttrs.TryGetProperty("href", out var hProp))
                        {
                            linkHref = hProp.GetString();
                        }
                    }
                }

                if (isCode) text = $"`{text}`";
                if (isBold) text = $"**{text}**";
                if (isItalic) text = $"*{text}*";
                if (isStrike) text = $"~~{text}~~";
                if (linkHref != null) text = $"[{text}]({linkHref})";
            }

            sb.Append(text);
        }
        else if (type == "hardBreak")
        {
            sb.AppendLine();
        }
        else if (type == "mention")
        {
            var mentionText = "@user";
            if (node.TryGetProperty("attrs", out var mAttrs) && mAttrs.TryGetProperty("text", out var t))
            {
                mentionText = t.GetString() ?? "@user";
            }
            sb.Append(mentionText);
        }
    }
}
