using BackendDotnet.Models;
using Dapper;

namespace BackendDotnet.Services;

public sealed class AdmissionStructuredQueryService : IStructuredQueryService
{
    private readonly IDbConnectionFactory _db;

    public AdmissionStructuredQueryService(IDbConnectionFactory db) => _db = db;

    public async Task<StructuredResult> QueryAsync(string question, string tenantId, CancellationToken ct)
    {
        var q = question.ToLowerInvariant();

        // Phát hiện câu hỏi về điểm chuẩn
        if (q.Contains("điểm chuẩn") || q.Contains("điểm"))
        {
            return await QueryAdmissionScore(q);
        }

        // Phát hiện câu hỏi về học phí
        if (q.Contains("học phí") || q.Contains("tiền học"))
        {
            return await QueryTuition(q);
        }

        // Phát hiện câu hỏi về phương thức xét tuyển
        if (q.Contains("phương thức") || q.Contains("xét tuyển"))
        {
            return await QueryAdmissionMethods();
        }

        // Phát hiện câu hỏi về lịch trình
        if (q.Contains("lịch trình") || q.Contains("lịch"))
        {
            return QuerySchedule();
        }

        return new StructuredResult { HasResult = false };
    }

    private async Task<StructuredResult> QueryAdmissionScore(string q)
    {
        using var conn = _db.CreateConnection();

        // Tìm ngành được nhắc đến trong câu hỏi
        var majorName = ExtractMajor(q);
        var year = ExtractYear(q);

        string sql;
        object parameters;

        if (!string.IsNullOrEmpty(majorName) && year.HasValue)
        {
            sql = @"SELECT m.name, a.year, a.min_score, a.subject_combination, a.quota
                    FROM admission_scores a
                    JOIN majors m ON m.id = a.major_id
                    WHERE m.name ILIKE @major AND a.year = @year
                    ORDER BY a.year";
            parameters = new { major = $"%{majorName}%", year = year.Value };
        }
        else if (!string.IsNullOrEmpty(majorName))
        {
            sql = @"SELECT m.name, a.year, a.min_score, a.subject_combination, a.quota
                    FROM admission_scores a
                    JOIN majors m ON m.id = a.major_id
                    WHERE m.name ILIKE @major
                    ORDER BY a.year";
            parameters = new { major = $"%{majorName}%" };
        }
        else if (year.HasValue)
        {
            sql = @"SELECT m.name, a.year, a.min_score, a.subject_combination, a.quota
                    FROM admission_scores a
                    JOIN majors m ON m.id = a.major_id
                    WHERE a.year = @year
                    ORDER BY m.name, a.year";
            parameters = new { year = year.Value };
        }
        else
        {
            return new StructuredResult { HasResult = false };
        }

        var rows = await conn.QueryAsync(sql, parameters);
        var rowsList = rows.ToList();
        if (rowsList.Count == 0)
            return new StructuredResult { HasResult = false };

        var answer = string.Join("; ", rowsList.Select(r =>
            $"{r.name} ({r.year}): {r.min_score} điểm, tổ hợp {r.subject_combination}, quy mô {r.quota}"));

        return new StructuredResult
        {
            HasResult = true,
            Answer = answer,
            Metadata = new Dictionary<string, object> { ["type"] = "admission_scores", ["count"] = rowsList.Count },
        };
    }

    private async Task<StructuredResult> QueryTuition(string q)
    {
        using var conn = _db.CreateConnection();

        var majorName = ExtractMajor(q);
        string sql;
        object? parameters;

        if (!string.IsNullOrEmpty(majorName))
        {
            sql = @"SELECT m.name, t.tuition_per_year, t.currency, t.scholarship_info
                    FROM tuition_scholarships t
                    JOIN majors m ON m.id = t.major_id
                    WHERE m.name ILIKE @major";
            parameters = new { major = $"%{majorName}%" };
        }
        else
        {
            sql = @"SELECT m.name, t.tuition_per_year, t.currency, t.scholarship_info
                    FROM tuition_scholarships t
                    JOIN majors m ON m.id = t.major_id";
            parameters = null;
        }

        var rows = parameters != null
            ? (await conn.QueryAsync(sql, parameters)).ToList()
            : (await conn.QueryAsync(sql)).ToList();

        if (rows.Count == 0)
            return new StructuredResult { HasResult = false };

        var answer = string.Join("; ", rows.Select(r =>
            $"{r.name}: {r.tuition_per_year} {r.currency}/năm. {r.scholarship_info}"));

        return new StructuredResult
        {
            HasResult = true,
            Answer = answer,
            Metadata = new Dictionary<string, object> { ["type"] = "tuition", ["count"] = rows.Count },
        };
    }

    private async Task<StructuredResult> QueryAdmissionMethods()
    {
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync("SELECT method_name, description, requirements FROM admission_methods")).ToList();
        if (rows.Count == 0)
            return new StructuredResult { HasResult = false };

        var answer = string.Join("; ", rows.Select(r => $"{r.method_name}: {r.description} ({r.requirements})"));
        return new StructuredResult
        {
            HasResult = true,
            Answer = answer,
            Metadata = new Dictionary<string, object> { ["type"] = "admission_methods", ["count"] = rows.Count },
        };
    }

    private StructuredResult QuerySchedule()
    {
        return new StructuredResult
        {
            HasResult = true,
            Answer = "Lịch trình tuyển sinh 2026: Đăng ký xét tuyển tháng 5-6/2026, thi THPTQG tháng 6/2026, công bố kết quả tháng 7/2026, nhập học tháng 8-9/2026.",
            Metadata = new Dictionary<string, object> { ["type"] = "schedule", ["year"] = 2026 },
        };
    }

    private static string? ExtractMajor(string q)
    {
        var majors = new[]
        {
            "công nghệ thông tin", "khoa học máy tính", "hệ thống thông tin", "kỹ thuật phần mềm",
            "an toàn thông tin", "kinh doanh", "kinh tế", "tài chính ngân hàng", "kế toán",
            "ngôn ngữ anh", "ngôn ngữ hàn quốc", "ngôn ngữ nhật bản", "truyền thông đa phương tiện", "quảng cáo",
        };
        foreach (var m in majors)
            if (q.Contains(m))
                return m;
        return null;
    }

    private static int? ExtractYear(string q)
    {
        foreach (var y in new[] { 2023, 2024, 2025, 2026 })
            if (q.Contains(y.ToString()))
                return y;
        return null;
    }
}
