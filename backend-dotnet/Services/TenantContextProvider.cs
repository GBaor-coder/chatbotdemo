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
                TenantId = _config["DEFAULT_TENANT_ID"] ?? "11111111-1111-1111-1111-111111111111",
                UserId = "dev-user",
                OrganizationIds = Array.Empty<string>(),
            };
        }

        var tenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? _config["DEFAULT_TENANT_ID"] ?? "11111111-1111-1111-1111-111111111111";
        var userId = ctx.Request.Headers["X-User-Id"].FirstOrDefault() ?? "anonymous";

        return new TenantContext
        {
            TenantId = tenantId,
            UserId = userId,
            OrganizationIds = Array.Empty<string>(),
        };
    }
}
