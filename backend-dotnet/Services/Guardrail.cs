using BackendDotnet.Models;

namespace BackendDotnet.Services;

public sealed class Guardrail : IGuardrail
{
    private readonly string _refusalMessage;
    private readonly double _minScore;

    public Guardrail(IConfiguration config)
    {
        // Câu từ chối từ cấu hình theo tenant (không hard-code)
        _refusalMessage = config["GUARDRAIL_REFUSAL_MESSAGE"]
            ?? "Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!";
        _minScore = double.TryParse(config["GUARDRAIL_MIN_SCORE"], out var ms) ? ms : 0.45;
    }

    public bool ShouldRefuse(List<ChunkResult> chunks, StructuredResult structured)
    {
        var hasRelevantChunk = chunks.Any(c => c.Score >= _minScore);
        var hasStructured = structured.HasResult;
        return !hasRelevantChunk && !hasStructured;
    }

    public string GetRefusalMessage() => _refusalMessage;
}
