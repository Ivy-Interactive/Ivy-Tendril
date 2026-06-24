namespace Ivy.Plugins.Inbox;

/// <summary>
/// Allows plugins to add items to the Tendril Inbox, triggering plan creation.
/// </summary>
public interface IInbox
{
    /// <summary>Add a simple text description to the inbox.</summary>
    void Add(string description);

    /// <summary>Add a structured inbox item with metadata.</summary>
    void Add(InboxItem item);

    /// <summary>Add multiple items at once.</summary>
    void AddRange(IEnumerable<InboxItem> items);
}
