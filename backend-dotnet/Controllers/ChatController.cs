using BackendDotnet.Models;
using BackendDotnet.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace BackendDotnet.Controllers;

[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public sealed class ChatController : ControllerBase
{
    private readonly ChatService _chat;

    public ChatController(ChatService chat) => _chat = chat;

    [HttpPost("stream")]
    public async Task Stream(CancellationToken ct)
    {
        // Không đọc userId/tenantId từ body — lấy từ danh tính đã xác thực (header)
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        var sessionId = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
        // userId BỎ QUA nếu client gửi — lấy từ ITenantContext (header/X-User-Id)

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache, no-transform");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var chunk in _chat.StreamChatAsync(message, sessionId, ct).WithCancellation(ct))
        {
            var data = JsonSerializer.Serialize(chunk);
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
