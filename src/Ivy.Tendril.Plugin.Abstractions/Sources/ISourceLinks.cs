namespace Ivy.Plugins.Sources;

/// <summary>
/// Lets plugins turn a plan's source URL into a short human-facing label — the text Tendril
/// shows instead of a raw URL (e.g. "IVY-456" for a Linear issue). Tendril deliberately knows
/// no issue tracker's URL format; the plugin that owns the tracker owns the parsing.
/// </summary>
public interface ISourceLinks
{
    /// <summary>
    /// Registers a resolver. Return the label for URLs this plugin recognizes, or <c>null</c>
    /// for anything it doesn't own so other plugins get a turn. Resolvers must not throw,
    /// block, or perform I/O — they run inline while a job's prompt is being built.
    /// </summary>
    void RegisterResolver(Func<Uri, string?> resolver);
}
