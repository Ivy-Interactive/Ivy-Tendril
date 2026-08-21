using Ivy.Tendril.Services.Tunnel;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Services.Share;

public class ShareContext : IShareContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IShareTunnelService? _shareTunnelService;
    private string? _persona;
    private bool? _isShareMode;

    public ShareContext(
        IHttpContextAccessor? httpContextAccessor = null,
        IShareTunnelService? shareTunnelService = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _shareTunnelService = shareTunnelService;
    }

    public bool IsShareMode
    {
        get
        {
            if (_isShareMode.HasValue) return _isShareMode.Value;

            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext == null)
            {
                _isShareMode = false;
                return false;
            }

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

            var httpContext = _httpContextAccessor?.HttpContext;
            var seed = httpContext?.Connection.Id
                ?? httpContext?.Connection.RemoteIpAddress?.ToString()
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
