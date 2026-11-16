using BackendDotnet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace BackendDotnet.Controllers;

[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public sealed class HistoryController : ControllerBase
{
    private readonly ChatService _chat;
    private readonly ITenantContext _tenant;

    public HistoryController(ChatService chat, ITenantContext tenant)
    {
        _chat = chat;
        _tenant = tenant;
    }

    [HttpGet("history/{sessionId}")]
    public async Task<IActionResult> Get(string sessionId)
    {
        var ctx = _tenant.GetContext();
        if (string.IsNullOrEmpty(ctx.TenantId))
            return Unauthorized(new { error = "Không xác định được tenant." });

        var messages = await _chat.GetHistoryAsync(sessionId, ctx.TenantId);
        if (messages == null || messages.Count == 0)
        {
            // Không phân biệt 403/404 để tránh thông tin rò rãi (IDOR protection)
            return NotFound(new { error = "Không tìm thấy phiên hoặc không có quyền truy cập." });
        }

        return Ok(messages);
    }
}
