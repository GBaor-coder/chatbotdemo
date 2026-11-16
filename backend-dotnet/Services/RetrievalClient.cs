using BackendDotnet.Models;
using System.Linq;
using System.Text.Json;

namespace BackendDotnet.Services;

public sealed class RetrievalClient : IRetrievalClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public RetrievalClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["X_INTERNAL_API_KEY"] ?? "dev-internal-key";
        _http.BaseAddress = new Uri(config["PYTHON_RAG_URL"] ?? "http://ai-service:8000");
    }

    public async Task<RetrievalResponse> SearchAsync(
        string query, string tenantId, int topK, double minScore,
        Dictionary<string, object> filters, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tenantId))
            throw new ArgumentException("tenant_id is required", nameof(tenantId));

        var requestFilters = new Dictionary<string, object>(filters) { ["tenant_id"] = tenantId };
        var payload = new
        {
            query = query,
            top_k = topK,
            min_score = minScore,
            filters = requestFilters,
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/retrieval/search")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("X-Internal-Api-Key", _apiKey);
        req.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Python retrieval returned {(int)resp.StatusCode}: {body}");
        }

        var bodyStr = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(bodyStr);
        var root = doc.RootElement;

        var results = new List<ChunkResult>();
        if (root.TryGetProperty("results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultsEl.EnumerateArray())
            {
                results.Add(new ChunkResult
                {
                    ChunkId = item.GetProperty("chunk_id").GetString() ?? string.Empty,
                    DocumentId = item.GetProperty("document_id").GetString() ?? string.Empty,
                    Content = item.GetProperty("content").GetString() ?? string.Empty,
                    Score = item.GetProperty("score").GetDouble(),
                    Metadata = item.TryGetProperty("metadata", out var md)
                        ? md.EnumerateObject().ToDictionary(p => p.Name, p => (object)MetadataValueToString(p.Value))
                        : new Dictionary<string, object>(),
                });
            }
        }

        return new RetrievalResponse
        {
            Success = root.TryGetProperty("success", out var s) && s.GetBoolean(),
            Results = results,
            Returned = results.Count,
        };
    }

    private static string MetadataValueToString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.GetDouble().ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Array => string.Join(",", el.EnumerateArray().Select(MetadataValueToString)),
            JsonValueKind.Object => el.GetRawText(),
            _ => el.GetRawText(),
        };
    }
}
