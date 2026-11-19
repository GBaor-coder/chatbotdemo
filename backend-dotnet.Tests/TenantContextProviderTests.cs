using BackendDotnet.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace BackendDotnet.Tests;

public class TenantContextProviderTests
{
    [Fact]
    public void GetContext_Unauthenticated_ReturnsEmptyTenantAndUser()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var provider = new TenantContextProvider(http.Object, new ConfigurationBuilder().Build());

        var ctx = provider.GetContext();

        Assert.Equal(string.Empty, ctx.TenantId);
        Assert.Equal(string.Empty, ctx.UserId);
        Assert.Empty(ctx.OrganizationIds);
    }

    [Fact]
    public void GetContext_Authenticated_ReadsTenantAndUserFromClaims()
    {
        var claims = new List<Claim>
        {
            new Claim("tenant_id", "tenant-abc"),
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
        };
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(x => x.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        });
        var provider = new TenantContextProvider(http.Object, new ConfigurationBuilder().Build());

        var ctx = provider.GetContext();

        Assert.Equal("tenant-abc", ctx.TenantId);
        Assert.Equal("user-123", ctx.UserId);
    }
}
