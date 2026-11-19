using BackendDotnet.Models;
using Dapper;
using System.Text.Json;
using Polly;
using Polly.Retry;

namespace BackendDotnet.Services;

public sealed class ChatService : IChatStore
{
    private readonly IDbConnectionFactory _db;
    private readonly IRetrievalClient _retrieval;
    private readonly ILlmClient _llm;
    private readonly IGuardrail _guardrail;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IStructuredQueryService _structured;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;

    private static readonly AsyncRetryPolicy PythonRetryPolicy =
        Policy.Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(500 * retryAttempt));

    public ChatService(
        IDbConnectionFactory db,
        IRetrievalClient retrieval,
        ILlmClient llm,
        IGuardrail guardrail,
        IPromptBuilder promptBuilder,
        IStructuredQueryService structured,
        IConfiguration config,
        IHttpClientFactory http)
    {
        _db = db;
        _retrieval = retrieval;
        _llm = llm;
        _guardrail = guardrail;
        _promptBuilder = promptBuilder;
        _structured = structured;
        _config = config;
        _http = http;
    }

    public async Task<string> EnsureSessionAsync(string? sessionId, string tenantId, string userId, string title)
    {
        using var conn = _db.CreateConnection();
        if (!string.IsNullOrEmpty(sessionId))
        {
            var existing = await conn.QueryFirstOrDefaultAsync<ChatSessionDto>(
                "SELECT CAST(id AS text) AS Id, title, CAST(tenant_id AS text) AS TenantId, CAST(user_id AS text) AS UserId, created_at AS CreatedAt, updated_at AS UpdatedAt FROM chat_sessions WHERE id = @id::uuid AND deleted_at IS NULL",
                new { id = sessionId });
            if (existing != null) return existing.Id;
        }

        var id = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO chat_sessions (id, tenant_id, user_id, title, created_at, updated_at) VALUES (@id::uuid, @tenantId::uuid, @userId, @title, NOW(), NOW())",
            new { id, tenantId, userId, title });
        return id;
    }

    public async Task SaveMessageAsync(string sessionId, string role, string content, List<object>? sources = null, string? metadataJson = null)
    {
        using var conn = _db.CreateConnection();
        var citationsJson = sources != null ? JsonSerializer.Serialize(sources) : "[]";
        await conn.ExecuteAsync(
            "INSERT INTO chat_messages (id, session_id, role, content, citations, metadata, created_at) VALUES (@id::uuid, @sessionId::uuid, @role, @content, @citations::jsonb, @metadata::jsonb, NOW())",
            new { id = Guid.NewGuid().ToString(), sessionId, role, content, citations = citationsJson, metadata = metadataJson ?? "{}" });
    }

    public async Task<List<ChatMessageDto>> GetHistoryAsync(string sessionId, string tenantId)
    {
        using var conn = _db.CreateConnection();
        var owner = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM chat_sessions WHERE id = @sid::uuid AND tenant_id = @tid::uuid AND deleted_at IS NULL",
            new { sid = sessionId, tid = tenantId });
        if (owner == null)
            return new List<ChatMessageDto>();

        var rows = await conn.QueryAsync<ChatMessageDto>(
            "SELECT CAST(id AS text) AS Id, CAST(session_id AS text) AS SessionId, role, content, metadata AS Metadata, created_at AS CreatedAt FROM chat_messages WHERE session_id = @sid::uuid AND deleted_at IS NULL ORDER BY created_at ASC",
            new { sid = sessionId });
        return rows.ToList();
    }
}

public sealed class ChatSessionDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
