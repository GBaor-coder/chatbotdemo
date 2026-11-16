using BackendDotnet.Models;

namespace BackendDotnet.Services;

public sealed class PromptBuilder : IPromptBuilder
{
    public string Build(string query, List<ChunkResult> chunks, StructuredResult? structured)
    {
        var ctxParts = new List<string>();
        int i = 1;
        foreach (var c in chunks)
        {
            var title = c.Metadata.TryGetValue("title", out var t) ? t?.ToString() ?? "Không tên" : "Không tên";
            var source = c.Metadata.TryGetValue("source", out var s) ? s?.ToString() ?? "N/A" : "N/A";
            ctxParts.Add($"[Nguồn {i}] {title} (nguồn: {source})\n{c.Content}");
            i++;
        }

        var context = ctxParts.Count > 0 ? string.Join("\n\n", ctxParts) : "(Không có ngữ cảnh liên quan.)";

        var structuredInfo = structured != null && structured.HasResult
            ? $"\nDỮ LIỆU CẤU TRÚC:\n{structured.Answer}"
            : "";

        return $@"Bạn là trợ lý ảo tư vấn tuyển sinh Đại học Việt Nam.

NGUYÊN TẮT CẤN TRỌNG:
- Chỉ trả lời dựa trên NGỮ CẢNH và DỮ LIỆU CẤU TRÚC được cung cấp bên dưới. Không bịa đặt, không suy luận ngoài ngữ cảnh.
- Nếu ngữ cảnh và dữ liệu cấu trúc không chứa thông tin đủ để trả lời, hãy thông báo rằng bạn không có đủ thông tin.
- Khi trả lời, hãy dẫn nguồn trích dẫn từ từng đoạn ngữ cảnh được cung cấp.
- Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng, đúng trọng tâm câu hỏi.

NGỮ CẢNH:
{context}{structuredInfo}

CÂU HỎI: {query}

TRẢ LỜI:";
    }
}
