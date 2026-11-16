using BackendDotnet.Models;
using Microsoft.AspNetCore.Http;

namespace BackendDotnet.Services;

// [GIẢ ĐỊNH] Dev-only: lấy tenant_id / user_id từ header authentication.
// Trong production, thay bằng việc đọc từ JWT claim sau khi xác thực thực tế.
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
        var ctx = _http.HttpContext;
        if (ctx == null)
        {
            return new TenantContext
            {
                TenantId = string.Empty,
                UserId = string.Empty,
                OrganizationIds = Array.Empty<string>(),
            };
        }

        var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
        var xTenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        // [GIẢ ĐỊNH] Dev auth: Yêu cầu Authorization header (hoặc X-Tenant-Id) để xác thực.
        // Nếu không có cả hai -> coi như unauthenticated (trả TenantId rỗng để controller trả 401).
        if (string.IsNullOrEmpty(authHeader) && string.IsNullOrEmpty(xTenantId))
        {
            return new TenantContext
            {
                TenantId = string.Empty,
                UserId = string.Empty,
                OrganizationIds = Array.Empty<string>(),
            };
        }

        var tenantId = xTenantId
            ?? _config["DEFAULT_TENANT_ID"] ?? "11111111-1111-1111-1111-111111111111";
        var userId = ctx.Request.Headers["X-User-Id"].FirstOrDefault() ?? "authenticated-user";

        return new TenantContext
        {
            TenantId = tenantId,
            UserId = userId,
            OrganizationIds = Array.Empty<string>(),
        };
    }
}
