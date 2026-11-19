using BackendDotnet.Hubs;
using BackendDotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Reflection;
using Xunit;

namespace BackendDotnet.Tests;

public class ChatHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChatHubTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Hub_HasAuthorizeAttribute()
    {
        var attr = typeof(ChatHub).GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        Assert.NotNull(attr);
    }

    [Fact]
    public void StreamChatRequest_CannotCarryTenantId_UserId()
    {
        var props = typeof(StreamChatRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();
        Assert.Equal(new HashSet<string> { "SessionId", "Message" }, props);
    }

    [Fact]
    public async Task Negotiate_WithoutJwtToken_Returns401()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsync("/hubs/chat/negotiate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
