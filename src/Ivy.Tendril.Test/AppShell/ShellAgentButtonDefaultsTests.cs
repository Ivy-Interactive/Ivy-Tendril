using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test.AppShell;

public class ShellAgentButtonDefaultsTests
{
    [Fact]
    public void ShortcutKey_Default_ReturnsA()
    {
        // Contract pin: the letter "A" is the value both C# and TSX sides agree on.
        // This test verifies the default value — it does NOT cover the chord behavior
        // (Cmd+Opt+A / Ctrl+Alt+A), which is exercised by ShellAgentButton.test.tsx.
        Assert.Equal("A", new ShellAgentButton().ShortcutKey);
    }
}
