using BackendDotnet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BackendDotnet.Controllers;

[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public sealed class HistoryController : ControllerBase
{
    private readonly HistoryService _history;

    public HistoryController(HistoryService history) => _history = history;

    [HttpGet("history/{sessionId}")]
    public async Task<IActionResult> Get(string sessionId)
    {
        var messages = await _history.GetMessagesAsync(sessionId);
        return Ok(messages);
    }
}
