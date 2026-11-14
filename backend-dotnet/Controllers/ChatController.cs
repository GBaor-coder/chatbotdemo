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
    public async Task Stream([FromBody] StreamRequest req, CancellationToken ct)
    {
        // Tắt response buffering để pipe SSE real-time từ FastAPI về React
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache, no-transform");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var chunk in _chat.StreamFromAiServiceAsync(req.Message, req.SessionId, req.UserId).WithCancellation(ct))
        {
            var data = JsonSerializer.Serialize(chunk);
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}

public sealed class StreamRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string UserId { get; set; } = "anonymous";
}
