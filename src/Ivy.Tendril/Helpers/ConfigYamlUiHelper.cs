using Ivy.Core.Apps;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Opens <c>config.yaml</c> in the host-appropriate UI: desktop shell or local browser
/// (<c>localhost</c> / loopback Host) uses the configured external editor; remote web uses the in-app editor.
/// </summary>
public static class ConfigYamlUiHelper
{
    /// <summary>
    /// Call this overload only from <c>Build()</c> — HttpContext is available there.
    /// Captures the host immediately so the click handler doesn't rely on HttpContext.
    /// </summary>
    public static void OpenOrNavigate(
        IConfigService config,
        INavigator navigator,
        bool isDesktopShell,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var capturedHost = httpContextAccessor?.HttpContext?.Request.Host.Host;
        OpenOrNavigate(config, navigator, isDesktopShell, capturedHost);
    }

    /// <summary>
    /// Use this overload inside click handlers — pass the host captured during <c>Build()</c>.
    /// </summary>
    public static void OpenOrNavigate(
        IConfigService config,
        INavigator navigator,
        bool isDesktopShell,
        string? capturedHost)
    {
        if (isDesktopShell || IsLoopbackHost(capturedHost))
            config.OpenInEditor(config.ConfigPath);
        else
            navigator.Navigate<ConfigEditorApp>();
    }

    /// <summary>
    /// Captures the request host during Build/render so it can be safely used inside click handlers.
    /// </summary>
    public static string? CaptureHost(IHttpContextAccessor? httpContextAccessor) =>
        httpContextAccessor?.HttpContext?.Request.Host.Host;

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("::1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
