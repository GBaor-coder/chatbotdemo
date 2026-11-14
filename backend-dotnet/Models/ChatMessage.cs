namespace BackendDotnet.Models;

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
