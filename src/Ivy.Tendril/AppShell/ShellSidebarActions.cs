namespace Ivy.Tendril.AppShell;

public static class ShellSidebarActions
{
    public const string Execute = "execute";
    public const string CreatePr = "create-pr";
}

public record ShellSidebarActionRequest(string ItemId, string ActionId);
