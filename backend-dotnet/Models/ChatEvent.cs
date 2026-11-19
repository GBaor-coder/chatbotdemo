using System.Text.Json.Serialization;

namespace BackendDotnet.Models;

public sealed class ChatEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // token | citations | done | error

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("sources")]
    public List<object>? Sources { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
}
