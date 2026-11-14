using System.Text.Json.Serialization;

namespace BackendDotnet.Models;

public sealed class StreamChunk
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "token";
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    [JsonPropertyName("sources")]
    public List<object>? Sources { get; set; }
    [JsonPropertyName("done")]
    public bool Done { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
}
