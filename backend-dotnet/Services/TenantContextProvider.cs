using BackendDotnet.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace BackendDotnet.Services;

// Xác thực qua JWT (access_token đọc từ query string cho /hubs/chat qua JwtBearerEvents.OnMessageReceived).
// tenant_id / userId lấy từ claims của Context.User, KHÔNG tin payload do client gửi.
public sealed class TenantContextProvider : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _config;

    public TenantContextProvider(IHttpContextAccessor http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public TenantContext GetContext()
    {
        var user = _http.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
        {
            return new TenantContext
            {
                TenantId = string.Empty,
                UserId = string.Empty,
                OrganizationIds = Array.Empty<string>(),
            };
        }

        var tenantId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tid")?.Value
            ?? string.Empty;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? string.Empty;

        return new TenantContext
        {
            TenantId = tenantId,
            UserId = userId,
            OrganizationIds = Array.Empty<string>(),
        };
    }
}
