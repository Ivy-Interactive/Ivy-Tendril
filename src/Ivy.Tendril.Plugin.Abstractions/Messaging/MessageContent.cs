namespace Ivy.Plugins.Messaging;

/// <summary>
/// Base type for structured message content nodes.
///
/// Hosts should always handle unknown subtypes gracefully by falling back to
/// <see cref="FallbackText"/> when encountering a node type they don't recognize.
/// </summary>
public abstract record MessageContent
{
    /// <summary>
    /// Plain-text fallback for hosts that don't recognize this content node type.
    /// Subtypes should override this to provide a meaningful text representation.
    /// </summary>
    public virtual string? FallbackText => null;
}

public sealed record TextNode(string Text) : MessageContent
{
    public override string? FallbackText => Text;
}

public sealed record BoldNode(MessageContent Content) : MessageContent
{
    public override string? FallbackText => Content.FallbackText;
}

public sealed record ItalicNode(MessageContent Content) : MessageContent
{
    public override string? FallbackText => Content.FallbackText;
}

public sealed record StrikethroughNode(MessageContent Content) : MessageContent
{
    public override string? FallbackText => Content.FallbackText;
}

public sealed record CodeNode(string Code) : MessageContent
{
    public override string? FallbackText => Code;
}

public sealed record CodeBlockNode(string Code, string? Language = null) : MessageContent
{
    public override string? FallbackText => Code;
}

public sealed record LinkNode(string Url, string? Label = null) : MessageContent
{
    public override string? FallbackText => Label ?? Url;
}

public sealed record ImageNode(string Url, string AltText) : MessageContent
{
    public override string? FallbackText => AltText;
}

public sealed record LineBreakNode : MessageContent
{
    public static readonly LineBreakNode Instance = new();
    public override string? FallbackText => "\n";
}

public sealed record DividerNode : MessageContent
{
    public static readonly DividerNode Instance = new();
    public override string? FallbackText => "---";
}

public sealed record SectionNode(MessageContent Content, ImageNode? Accessory = null) : MessageContent
{
    public override string? FallbackText => Content.FallbackText;
}

public sealed record SequenceNode(IReadOnlyList<MessageContent> Children) : MessageContent
{
    public override string? FallbackText => string.Join("", Children.Select(c => c.FallbackText ?? ""));
}
