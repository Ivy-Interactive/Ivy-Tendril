namespace Ivy.Plugins.Messaging;

public class MessageBuilder
{
    private readonly List<MessageContent> _nodes = [];
    private readonly List<FileAttachment> _attachments = [];

    public MessageBuilder Text(string text)
    {
        _nodes.Add(new TextNode(text));
        return this;
    }

    public MessageBuilder Bold(string text) => Bold(b => b.Text(text));

    public MessageBuilder Bold(Action<MessageBuilder> configure)
    {
        _nodes.Add(new BoldNode(Build(configure)));
        return this;
    }

    public MessageBuilder Italic(string text) => Italic(b => b.Text(text));

    public MessageBuilder Italic(Action<MessageBuilder> configure)
    {
        _nodes.Add(new ItalicNode(Build(configure)));
        return this;
    }

    public MessageBuilder Strikethrough(string text) => Strikethrough(b => b.Text(text));

    public MessageBuilder Strikethrough(Action<MessageBuilder> configure)
    {
        _nodes.Add(new StrikethroughNode(Build(configure)));
        return this;
    }

    public MessageBuilder Code(string code)
    {
        _nodes.Add(new CodeNode(code));
        return this;
    }

    public MessageBuilder CodeBlock(string code, string? language = null)
    {
        _nodes.Add(new CodeBlockNode(code, language));
        return this;
    }

    public MessageBuilder Link(string url, string? label = null)
    {
        _nodes.Add(new LinkNode(url, label));
        return this;
    }

    public MessageBuilder Image(string url, string altText)
    {
        _nodes.Add(new ImageNode(url, altText));
        return this;
    }

    public MessageBuilder LineBreak()
    {
        _nodes.Add(LineBreakNode.Instance);
        return this;
    }

    public MessageBuilder Divider()
    {
        _nodes.Add(DividerNode.Instance);
        return this;
    }

    public MessageBuilder Section(Action<MessageBuilder> configure, ImageNode? accessory = null)
    {
        _nodes.Add(new SectionNode(Build(configure), accessory));
        return this;
    }

    public MessageBuilder Attach(byte[] content, string fileName, string? title = null)
    {
        _attachments.Add(new FileAttachment(content, fileName, title));
        return this;
    }

    public Message Build(string? threadId = null)
    {
        return new Message
        {
            Content = Flatten(),
            ThreadId = threadId,
            Attachments = _attachments.Count > 0 ? _attachments.ToList() : null,
        };
    }

    private MessageContent Flatten()
    {
        return _nodes.Count == 1 ? _nodes[0] : new SequenceNode(_nodes.ToList());
    }

    private static MessageContent Build(Action<MessageBuilder> configure)
    {
        var builder = new MessageBuilder();
        configure(builder);
        return builder.Flatten();
    }
}
