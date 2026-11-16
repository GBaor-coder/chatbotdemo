using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BackendDotnet.Services;

public sealed class LlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;

    public LlmClient(IConfiguration config, IHttpClientFactory httpFactory)
    {
        var apiKey = config["LLM_API_KEY"] ?? throw new InvalidOperationException("LLM_API_KEY chưa được cấu hình");
        _baseUrl = config["LLM_BASE_URL"] ?? "http://host.docker.internal:20128/v1";
        _model = config["LLM_MODEL"] ?? "chatbotai";

        _http = httpFactory.CreateClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = prompt },
            },
            temperature = 0.3,
            stream = true,
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        string? error = null;
        HttpResponseMessage? resp = null;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error != null || resp == null)
        {
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (ct.IsCancellationRequested) break;
            if (line.StartsWith("data: "))
            {
                var payloadJson = line[6..].Trim();
                if (payloadJson == "[DONE]") break;
                string? contentDelta = null;
                try
                {
                    using var doc = JsonDocument.Parse(payloadJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("choices", out var choices))
                    {
                        foreach (var choice in choices.EnumerateArray())
                        {
                            if (choice.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("content", out var content))
                            {
                                contentDelta = content.GetString();
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Bỏ qua dòng SSE bị hỏng
                }
                if (contentDelta != null && contentDelta.Length > 0)
                    yield return contentDelta;
            }
        }
    }
}
