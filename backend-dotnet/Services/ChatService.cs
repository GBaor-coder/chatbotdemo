using BackendDotnet.Models;
using Dapper;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackendDotnet.Services;

public sealed class RagPayload
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "anonymous";
}

public sealed class RagTokenEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    [JsonPropertyName("sources")]
    public List<object>? Sources { get; set; }
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
}

public sealed class ChatService
{
    private readonly IDbConnectionFactory _db;
    private readonly IHttpClientFactory _http;

    public ChatService(IDbConnectionFactory db, IHttpClientFactory http)
    {
        _db = db;
        _http = http;
    }

    public async Task<string> EnsureSessionAsync(string? sessionId, string userId, string title)
    {
        using var conn = _db.CreateConnection();
        if (!string.IsNullOrEmpty(sessionId))
        {
            var existing = await conn.QueryFirstOrDefaultAsync<ChatSession>(
                "SELECT id, title, user_id AS UserId, created_at AS CreatedAt, updated_at AS UpdatedAt FROM chat_sessions WHERE id = @id::uuid AND deleted_at IS NULL",
                new { id = sessionId });
            if (existing != null) return existing.Id;
        }

        var id = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO chat_sessions (id, user_id, title, created_at, updated_at) VALUES (@id::uuid, @userId, @title, NOW(), NOW())",
            new { id, userId, title });
        return id;
    }

    public async Task SaveMessageAsync(string sessionId, string role, string content, string? metadataJson = null)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO chat_messages (id, session_id, role, content, metadata, created_at) VALUES (@id::uuid, @sessionId::uuid, @role, @content, @metadata::jsonb, NOW())",
            new { id = Guid.NewGuid().ToString(), sessionId, role, content, metadata = metadataJson });
    }

    public async IAsyncEnumerable<StreamChunk> StreamFromAiServiceAsync(string message, string? sessionId, string userId)
    {
        var client = _http.CreateClient();
        var resolvedSessionId = await EnsureSessionAsync(sessionId, userId, message.Length > 60 ? message[..60] : message);

        await SaveMessageAsync(resolvedSessionId, "user", message);

        var aiServiceUrl = Environment.GetEnvironmentVariable("AI_SERVICE_URL") ?? "http://ai-service:8000";
        var payload = new RagPayload { Message = message, SessionId = resolvedSessionId, UserId = userId };
        var json = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{aiServiceUrl}/rag/stream")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var fullAnswer = "";
        List<object> sources = new();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data: "))
            {
                var payloadJson = line[6..];
                RagTokenEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<RagTokenEvent>(payloadJson);
                }
                catch
                {
                    continue;
                }

                if (evt == null) continue;

                switch (evt.Type)
                {
                    case "token":
                        fullAnswer += evt.Content;
                        yield return new StreamChunk
                        {
                            Type = "token",
                            Content = evt.Content,
                            SessionId = resolvedSessionId
                        };
                        break;
                    case "citations":
                        sources = evt.Sources ?? new List<object>();
                        yield return new StreamChunk
                        {
                            Type = "citations",
                            Sources = sources,
                            SessionId = resolvedSessionId
                        };
                        break;
                    case "done":
                        yield return new StreamChunk
                        {
                            Type = "done",
                            Done = true,
                            SessionId = evt.SessionId ?? resolvedSessionId
                        };
                        break;
                    case "error":
                        yield return new StreamChunk
                        {
                            Type = "error",
                            Error = evt.Content,
                            SessionId = resolvedSessionId
                        };
                        break;
                }
            }
        }

        // Lưu câu trả lời hoàn chỉnh của bot vào DB
        var metadata = JsonSerializer.Serialize(new { sources });
        await SaveMessageAsync(resolvedSessionId, "assistant", fullAnswer, metadata);
    }
}
