using BackendDotnet.Models;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BackendDotnet.Services;

public sealed class ChatOrchestrator : IChatOrchestrator
{
    private const int MaxMessageLength = 2000;
    // Theo dõi phiên đang stream (chỉ từ chối stream thứ hai, không xếp hàng).
    // TryAdd/TryRemove là nguyên tử, xóa entry sau khi xong → không rò rỉ bộ nhớ theo số session.
    // Giới hạn này chỉ đúng trong một instance; chạy nhiều instance cần backplane/khóa phân tán.
    private static readonly ConcurrentDictionary<string, object> _sessionStreams = new();

    private readonly IChatStore _chat;
    private readonly IRetrievalClient _retrieval;
    private readonly ILlmClient _llm;
    private readonly IGuardrail _guardrail;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IStructuredQueryService _structured;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatOrchestrator> _logger;

    private static readonly AsyncRetryPolicy PythonRetryPolicy =
        Policy.Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(500 * retryAttempt));

    private static readonly object _streamMarker = new();

    public ChatOrchestrator(
        IChatStore chat,
        IRetrievalClient retrieval,
        ILlmClient llm,
        IGuardrail guardrail,
        IPromptBuilder promptBuilder,
        IStructuredQueryService structured,
        IConfiguration config,
        ILogger<ChatOrchestrator> logger)
    {
        _chat = chat;
        _retrieval = retrieval;
        _llm = llm;
        _guardrail = guardrail;
        _promptBuilder = promptBuilder;
        _structured = structured;
        _config = config;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatEvent> StreamChat(
        string? sessionId,
        string message,
        string tenantId,
        string userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            yield return new ChatEvent { Type = "error", Error = "Thông điệp không được để trống." };
            yield break;
        }

        if (message.Length > MaxMessageLength)
        {
            yield return new ChatEvent { Type = "error", Error = $"Thông điệp quá dài (tối đa {MaxMessageLength} ký tự)." };
            yield break;
        }

        if (string.IsNullOrEmpty(tenantId))
        {
            yield return new ChatEvent { Type = "error", Error = "Không xác định được phạm vi dữ liệu (tenant)." };
            yield break;
        }

        var resolvedSessionId = await _chat.EnsureSessionAsync(sessionId, tenantId, userId, message.Length > 60 ? message[..60] : message);
        if (!_sessionStreams.TryAdd(resolvedSessionId, _streamMarker))
        {
            yield return new ChatEvent
            {
                Type = "error",
                Error = "Phiên này đang có một lượt trả lời khác đang chạy. Vui lòng đợi xong rồi thử lại.",
                SessionId = resolvedSessionId,
            };
            yield break;
        }

        try
        {
            string? fatalError = null;

            try { await _chat.SaveMessageAsync(resolvedSessionId, "user", message); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lưu tin nhắn người dùng thất bại");
                fatalError = "Lỗi lưu tin nhắn.";
            }
            if (fatalError != null)
            {
                yield return new ChatEvent { Type = "error", Error = fatalError, SessionId = resolvedSessionId };
                yield break;
            }

            StructuredResult? structuredResult = null;
            try
            {
                structuredResult = await _structured.QueryAsync(message, tenantId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi truy vấn có cấu trúc");
            }

            List<ChunkResult> chunks = new();
            string? retrievalError = null;
            try
            {
                var filters = new Dictionary<string, object>();
                var retrievalMinScore = double.TryParse(_config["RETRIEVAL_MIN_SCORE"], out var rms) ? rms : 0.3;
                var retrievalResult = await PythonRetryPolicy.ExecuteAsync(async () =>
                    await _retrieval.SearchAsync(message, tenantId, 5, retrievalMinScore, filters, ct));
                chunks = retrievalResult.Results;
            }
            catch (Exception ex)
            {
                retrievalError = ex.Message;
            }
            if (retrievalError != null)
            {
                _logger.LogError("Lỗi truy xuất ngữ cảnh cho session {SessionId}: {Error}", resolvedSessionId, retrievalError);
                yield return new ChatEvent { Type = "error", Error = "Không thể truy xuất ngữ cảnh từ dịch vụ RAG. Vui lòng thử lại sau.", SessionId = resolvedSessionId };
                yield break;
            }

            var shouldRefuse = _guardrail.ShouldRefuse(chunks, structuredResult ?? new StructuredResult { HasResult = false });
            if (shouldRefuse)
            {
                var refusal = _guardrail.GetRefusalMessage();
                yield return new ChatEvent { Type = "token", Content = refusal, SessionId = resolvedSessionId };
                yield return new ChatEvent { Type = "citations", Sources = new List<object>(), SessionId = resolvedSessionId };
                yield return new ChatEvent { Type = "done", SessionId = resolvedSessionId, Done = true };
                await _chat.SaveMessageAsync(resolvedSessionId, "assistant", refusal, new List<object>());
                yield break;
            }

            var prompt = _promptBuilder.Build(message, chunks, structuredResult);
            var fullAnswer = "";
            var citations = chunks.Select(c => new
            {
                id = c.ChunkId,
                title = c.Metadata.TryGetValue("title", out var t) ? t?.ToString() : c.ChunkId,
                source = c.Metadata.TryGetValue("source", out var s) ? s?.ToString() : "",
                score = c.Score,
            }).ToList<object>();

            IAsyncEnumerable<string>? tokenStream = null;
            string? llmError = null;
            try
            {
                tokenStream = _llm.StreamCompletionAsync(prompt, ct);
            }
            catch (Exception ex)
            {
                llmError = ex.Message;
            }
            if (llmError != null)
            {
                yield return new ChatEvent { Type = "error", Error = "Lỗi LLM.", SessionId = resolvedSessionId };
                yield break;
            }

            var cancelled = false;
            if (tokenStream != null)
            {
                await foreach (var token in tokenStream)
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    fullAnswer += token;
                    yield return new ChatEvent { Type = "token", Content = token, SessionId = resolvedSessionId };
                }
            }

            if (cancelled)
            {
                _logger.LogInformation("Stream bị hủy bởi client cho session {SessionId}, không lưu assistant message.", resolvedSessionId);
                yield break;
            }

            yield return new ChatEvent { Type = "citations", Sources = citations, SessionId = resolvedSessionId };
            yield return new ChatEvent { Type = "done", SessionId = resolvedSessionId, Done = true };

            var metadata = JsonSerializer.Serialize(new { sources = citations });
            try
            {
                await _chat.SaveMessageAsync(resolvedSessionId, "assistant", fullAnswer, citations, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lưu tin nhắn trợ lý thất bại cho session {SessionId}", resolvedSessionId);
            }
        }
        finally
        {
            _sessionStreams.TryRemove(resolvedSessionId, out _);
        }
    }
}
