namespace Ivy.Plugins.Messaging;

public abstract record MessageContent;

public sealed record TextNode(string Text) : MessageContent;

public sealed record BoldNode(MessageContent Content) : MessageContent;

public sealed record ItalicNode(MessageContent Content) : MessageContent;

public sealed record StrikethroughNode(MessageContent Content) : MessageContent;

public sealed record CodeNode(string Code) : MessageContent;

public sealed record CodeBlockNode(string Code, string? Language = null) : MessageContent;

public sealed record LinkNode(string Url, string? Label = null) : MessageContent;

public sealed record ImageNode(string Url, string AltText) : MessageContent;

public sealed record LineBreakNode : MessageContent
{
    public static readonly LineBreakNode Instance = new();
}

public sealed record DividerNode : MessageContent
{
    public static readonly DividerNode Instance = new();
}

public sealed record SectionNode(MessageContent Content, ImageNode? Accessory = null) : MessageContent;

public sealed record SequenceNode(IReadOnlyList<MessageContent> Children) : MessageContent;
