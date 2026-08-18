namespace Ivy.Tendril.Test;

[CollectionDefinition("TendrilHome")]
public class TendrilHomeCollection;

public static class TestLocks
{
    public static readonly object ConsoleLock = new();
}
