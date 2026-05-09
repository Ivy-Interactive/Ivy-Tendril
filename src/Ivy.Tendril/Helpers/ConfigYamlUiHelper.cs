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
    public static void OpenOrNavigate(
        IConfigService config,
        INavigator navigator,
        bool isDesktopShell,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        if (isDesktopShell || IsLikelyLocalLoopbackBrowser(httpContextAccessor))
            config.OpenInEditor(config.ConfigPath);
        else
            navigator.Navigate<ConfigEditorApp>();
    }

    /// <summary>
    /// True when the HTTP Host is loopback (e.g. user opened <c>http://localhost:5010/</c> on the same machine
    /// that runs Tendril, so spawning <c>code</c> / the configured editor is meaningful).
    /// </summary>
    public static bool IsLikelyLocalLoopbackBrowser(IHttpContextAccessor? httpContextAccessor)
    {
        var host = httpContextAccessor?.HttpContext?.Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return false;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("::1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
