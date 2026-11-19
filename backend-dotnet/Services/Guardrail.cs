using BackendDotnet.Models;

namespace BackendDotnet.Services;

public sealed class Guardrail : IGuardrail
{
    private readonly string _refusalMessage;
    private readonly double _minScore;

    public Guardrail(IConfiguration config)
    {
        // Câu từ chối phải được cấu hình (không hard-code). Thiếu cấu hình → fail fast khi khởi động.
        _refusalMessage = config["GUARDRAIL_REFUSAL_MESSAGE"]
            ?? throw new InvalidOperationException("GUARDRAIL_REFUSAL_MESSAGE chưa được cấu hình.");
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
