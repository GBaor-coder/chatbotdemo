using BackendDotnet.Models;
using BackendDotnet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BackendDotnet.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class SuggestionsController : ControllerBase
{
    private static readonly List<Suggestion> _suggestions = new()
    {
        new Suggestion { Id = "s1", Question = "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?" },
        new Suggestion { Id = "s2", Question = "Học phí ngành Kinh doanh năm nay thế nào?" },
        new Suggestion { Id = "s3", Question = "Phương thức xét tuyển cho ngành Ngôn ngữ Anh như thế nào?" },
        new Suggestion { Id = "s4", Question = "Lịch trình tuyển sinh 2026 bắt đầu khi nào?" },
    };

    [HttpGet("suggestions")]
    public IActionResult Get() => Ok(_suggestions);
}
