using System.Text.Json.Serialization;

namespace BackendDotnet.Models;

public sealed class StreamChatRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
