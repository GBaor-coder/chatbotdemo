using BackendDotnet.Models;
using Dapper;

namespace BackendDotnet.Services;

public sealed class HistoryService
{
    private readonly IDbConnectionFactory _db;

    public HistoryService(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<ChatSession>> ListSessionsAsync(string userId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<ChatSession>(
            "SELECT id, title, user_id AS UserId, created_at AS CreatedAt, updated_at AS UpdatedAt FROM chat_sessions WHERE user_id = @uid AND deleted_at IS NULL ORDER BY updated_at DESC",
            new { uid = userId });
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string sessionId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<ChatMessage>(
            "SELECT id, session_id AS SessionId, role, content, metadata AS Metadata, created_at AS CreatedAt FROM chat_messages WHERE session_id = @sid::uuid AND deleted_at IS NULL ORDER BY created_at ASC",
            new { sid = sessionId });
    }
}
