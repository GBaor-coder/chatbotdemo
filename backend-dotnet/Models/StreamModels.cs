using System.Text.Json.Serialization;

namespace BackendDotnet.Models;

public sealed class TenantContext
{
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string[] OrganizationIds { get; init; } = Array.Empty<string>();
}

public sealed class ChunkResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public sealed class RetrievalResponse
{
    public bool Success { get; set; }
    public List<ChunkResult> Results { get; set; } = new();
    public int Returned { get; set; }
}

public sealed class StructuredResult
{
    public bool HasResult { get; set; }
    public string? Answer { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public sealed class ChatMessageDto
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class StreamEvent
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

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionIdCamel => SessionId;
}
