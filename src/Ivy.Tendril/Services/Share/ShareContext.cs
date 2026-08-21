using Ivy.Tendril.Services.Tunnel;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Services.Share;

public class ShareContext : IShareContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IShareTunnelService? _shareTunnelService;
    private readonly AppContext? _appContext;
    private string? _persona;
    private bool? _isShareMode;

    public ShareContext(
        IHttpContextAccessor? httpContextAccessor = null,
        IShareTunnelService? shareTunnelService = null,
        AppContext? appContext = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _shareTunnelService = shareTunnelService;
        _appContext = appContext;
    }

    public bool IsShareMode
    {
        get
        {
            if (_isShareMode.HasValue) return _isShareMode.Value;

            // 1. Check HttpContext if available
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                if (httpContext.Items.TryGetValue("IsShareMode", out var isShareObj) && isShareObj is true)
                {
                    _isShareMode = true;
                    return true;
                }

                if (httpContext.Request.Query.ContainsKey("share") ||
                    string.Equals(httpContext.Request.Query["mode"], "share", StringComparison.OrdinalIgnoreCase))
                {
                    _isShareMode = true;
                    return true;
                }

                if (httpContext.Request.Headers.ContainsKey("X-Tendril-Share"))
                {
                    _isShareMode = true;
                    return true;
                }

                if (httpContext.Request.Cookies.ContainsKey("tendril_share_mode"))
                {
                    _isShareMode = true;
                    return true;
                }
            }

            // 2. Check Ivy AppContext
            if (_appContext != null)
            {
                var args = _appContext.GetArgs<Dictionary<string, string>>();
                if (args != null && (args.ContainsKey("share") ||
                    string.Equals(args.GetValueOrDefault("mode"), "share", StringComparison.OrdinalIgnoreCase)))
                {
                    _isShareMode = true;
                    return true;
                }

                // If connecting through the active Share Tunnel URL, it is always Share Mode
                if (_shareTunnelService?.IsConnected == true && !string.IsNullOrEmpty(_shareTunnelService.TunnelUrl))
                {
                    if (Uri.TryCreate(_shareTunnelService.TunnelUrl, UriKind.Absolute, out var tunnelUri))
                    {
                        if (string.Equals(_appContext.Host, tunnelUri.Host, StringComparison.OrdinalIgnoreCase) ||
                            _appContext.Host.StartsWith(tunnelUri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            _isShareMode = true;
                            return true;
                        }
                    }
                }
            }

            _isShareMode = false;
            return false;
        }
    }

    public string Persona
    {
        get
        {
            if (!string.IsNullOrEmpty(_persona))
                return _persona;

            // 1. Check if persona was explicitly passed via cookie or query
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                if (httpContext.Request.Cookies.TryGetValue("tendril_persona", out var cookiePersona) && !string.IsNullOrWhiteSpace(cookiePersona))
                {
                    _persona = cookiePersona.Trim();
                    return _persona;
                }

                if (httpContext.Request.Query.TryGetValue("persona", out var queryPersona) && !string.IsNullOrWhiteSpace(queryPersona))
                {
                    _persona = queryPersona.ToString().Trim();
                    return _persona;
                }
            }

            // 2. Derive stable persona from MachineId (persisted per browser client)
            var seed = (!string.IsNullOrWhiteSpace(_appContext?.MachineId) ? _appContext.MachineId : null)
                ?? _httpContextAccessor?.HttpContext?.Request.Cookies["machineId"]
                ?? _httpContextAccessor?.HttpContext?.Request.Query["machineId"].ToString()
                ?? _appContext?.ConnectionId
                ?? _httpContextAccessor?.HttpContext?.Connection.Id
                ?? _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString()
                ?? Guid.NewGuid().ToString("N");

            _persona = AnonymousPersonaGenerator.Generate(seed);
            return _persona;
        }
    }

    public void SetPersona(string persona)
    {
        if (!string.IsNullOrWhiteSpace(persona))
        {
            _persona = persona.Trim();
        }
    }
}
