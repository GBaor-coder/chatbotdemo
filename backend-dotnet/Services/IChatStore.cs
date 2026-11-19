namespace BackendDotnet.Services;

public interface IChatStore
{
    Task<string> EnsureSessionAsync(string? sessionId, string tenantId, string userId, string title);
    Task SaveMessageAsync(string sessionId, string role, string content, List<object>? sources = null, string? metadataJson = null);
}
