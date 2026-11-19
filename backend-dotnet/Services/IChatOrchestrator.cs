using BackendDotnet.Models;

namespace BackendDotnet.Services;

public interface IChatOrchestrator
{
    IAsyncEnumerable<ChatEvent> StreamChat(string? sessionId, string message, string tenantId, string userId, CancellationToken ct);
}
