using BackendDotnet.Models;
using BackendDotnet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BackendDotnet.Hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IChatOrchestrator _orchestrator;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IChatOrchestrator orchestrator, ILogger<ChatHub> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatEvent> StreamChat(
        StreamChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value
            ?? string.Empty;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value
            ?? Context.User?.FindFirst("tid")?.Value
            ?? string.Empty;
        var message = request?.Message ?? string.Empty;
        var sessionId = request?.SessionId;

        _logger.LogDebug("StreamChat bắt đầu: userId={UserId}, tenantId={TenantId}, sessionId={SessionId}", userId, tenantId, sessionId);

        await foreach (var evt in _orchestrator.StreamChat(sessionId, message, tenantId, userId, ct))
        {
            yield return evt;
        }
    }
}
