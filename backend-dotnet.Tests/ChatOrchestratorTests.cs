using BackendDotnet.Models;
using BackendDotnet.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace BackendDotnet.Tests;

public class ChatOrchestratorTests
{
    private sealed class Mocks
    {
        public Mock<IChatStore> ChatStore { get; } = new();
        public Mock<IRetrievalClient> Retrieval { get; } = new();
        public Mock<ILlmClient> Llm { get; } = new();
        public Mock<IGuardrail> Guardrail { get; } = new();
        public Mock<IPromptBuilder> PromptBuilder { get; } = new();
        public Mock<IStructuredQueryService> Structured { get; } = new();
    }

    private (ChatOrchestrator Orch, Mocks M) Build(Action<Mocks>? setup = null, IEnumerable<KeyValuePair<string, string>>? config = null)
    {
        var m = new Mocks();

        m.ChatStore.Setup(x => x.EnsureSessionAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("sid-1");
        m.ChatStore.Setup(x => x.SaveMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<object>>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        m.Retrieval.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new RetrievalResponse
            {
                Results = new List<ChunkResult> { new ChunkResult { Score = 0.9 } }
            }));

        m.Guardrail.Setup(x => x.ShouldRefuse(It.IsAny<List<ChunkResult>>(), It.IsAny<StructuredResult>())).Returns(false);
        m.Guardrail.Setup(x => x.GetRefusalMessage()).Returns("Lời từ chối cấu hình.");

        m.PromptBuilder.Setup(x => x.Build(It.IsAny<string>(), It.IsAny<List<ChunkResult>>(), It.IsAny<StructuredResult?>())).Returns("prompt");

        m.Structured.Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructuredResult { HasResult = false });

        m.Llm.Setup(x => x.StreamCompletionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => TokensAsync(new[] { "x" }));

        var builder = new ConfigurationBuilder();
        if (config != null) builder.AddInMemoryCollection(config.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
        var cfg = builder.Build();

        var orch = new ChatOrchestrator(
            m.ChatStore.Object, m.Retrieval.Object, m.Llm.Object, m.Guardrail.Object,
            m.PromptBuilder.Object, m.Structured.Object, cfg,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChatOrchestrator>>().Object);

        setup?.Invoke(m);
        return (orch, m);
    }

    private static async IAsyncEnumerable<string> TokensAsync(string[] tokens)
    {
        foreach (var t in tokens) yield return t;
    }

    private static async Task<List<ChatEvent>> Collect(IAsyncEnumerable<ChatEvent> stream)
    {
        var list = new List<ChatEvent>();
        await foreach (var e in stream) list.Add(e);
        return list;
    }

    [Fact]
    public async Task Guardrail_Refuse_DoesNotCallLlm_AndSavesAssistant()
    {
        var (orch, m) = Build(m => {
            m.Guardrail.Setup(x => x.ShouldRefuse(It.IsAny<List<ChunkResult>>(), It.IsAny<StructuredResult>())).Returns(true);
            m.Retrieval.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new RetrievalResponse { Results = new List<ChunkResult>() }));
        });

        var events = await Collect(orch.StreamChat("sid", "thời tiết hôm nay", "tenant", "user", CancellationToken.None));

        // Không gọi LLM
        m.Llm.Verify(x => x.StreamCompletionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // Trả token từ chối, citations, done
        Assert.Contains(events, e => e.Type == "token" && e.Content == "Lời từ chối cấu hình.");
        Assert.Contains(events, e => e.Type == "citations");
        Assert.Contains(events, e => e.Type == "done" && e.Done);
        // Lưu assistant message (từ chối)
        m.ChatStore.Verify(x => x.SaveMessageAsync("sid-1", "assistant", "Lời từ chối cấu hình.", It.IsAny<List<object>>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancellationToken_Cancelled_DoesNotSaveAssistant()
    {
        var cts = new CancellationTokenSource();
        var (orch, m) = Build(m => {
            m.Llm.Setup(x => x.StreamCompletionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => SlowTokensAsync());
        });

        var events = new List<ChatEvent>();
        await foreach (var e in orch.StreamChat("sid", "hỏi điểm chuẩn", "tenant", "user", cts.Token))
        {
            events.Add(e);
            if (events.Count == 1) cts.Cancel(); // hủy sau token đầu tiên
        }

        Assert.DoesNotContain(events, e => e.Type == "done");
        // Không lưu assistant message khi bị hủy
        m.ChatStore.Verify(x => x.SaveMessageAsync(It.IsAny<string>(), "assistant", It.IsAny<string>(), It.IsAny<List<object>>(), It.IsAny<string>()), Times.Never);
    }

    private static async IAsyncEnumerable<string> SlowTokensAsync()
    {
        yield return "t1";
        await Task.Delay(100);
        yield return "t2";
    }

    [Fact]
    public async Task ConcurrentStreams_SameSession_ExactlyOneRejectedWithError()
    {
        var sid = "shared-sid";
        var (orch, m) = Build(mm => {
            mm.ChatStore.Setup(x => x.EnsureSessionAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(sid);
        });

        await using var e1 = orch.StreamChat(sid, "m1", "tenant", "user", CancellationToken.None).GetAsyncEnumerator();
        await using var e2 = orch.StreamChat(sid, "m2", "tenant", "user", CancellationToken.None).GetAsyncEnumerator();

        // Hai luồng chạy; entry được giữ bởi luồng đầu tiên → luồng thứ hai bị từ chối ngay.
        var t1 = e1.MoveNextAsync();
        var t2 = e2.MoveNextAsync();
        bool b1 = await t1;
        bool b2 = await t2;

        var firstEvents = new List<ChatEvent?>();
        if (b1) firstEvents.Add(e1.Current);
        if (b2) firstEvents.Add(e2.Current);

        var errors = firstEvents.OfType<ChatEvent>().Where(e => e.Type == "error").ToList();
        // Duy nhất một luồng bị lỗi "đang chạy", kia bắt đầu stream (token)
        Assert.Single(errors);
        Assert.Contains(errors, e => e.Error.Contains("đang chạy"));
        Assert.Single(firstEvents.OfType<ChatEvent>(), e => e.Type == "token");

        // Rãnh tài nguyên của luồng thua
        await e2.DisposeAsync();

        // Đưa luồng thắng đến hết → entry bị dọn → stream lại cùng session thành công
        while (await e1.MoveNextAsync()) { }
        await using var e3 = orch.StreamChat(sid, "m3", "tenant", "user", CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await e3.MoveNextAsync());
        Assert.Equal("token", e3.Current.Type);
    }

    [Fact]
    public async Task StreamCompletes_SessionEntryRemoved_CanStreamAgain()
    {
        var sid = "sid-cleanup";
        var (orch, m) = Build(m => {
            m.ChatStore.Setup(x => x.EnsureSessionAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(sid);
        });

        var first = await Collect(orch.StreamChat(sid, "m1", "tenant", "user", CancellationToken.None));
        Assert.Contains(first, e => e.Type == "done");

        // Sau khi stream xong, entry đã bị dọn → stream lại cùng session không bị lỗi "đang chạy"
        var second = await Collect(orch.StreamChat(sid, "m2", "tenant", "user", CancellationToken.None));
        Assert.DoesNotContain(second, e => e.Type == "error" && e.Error.Contains("đang chạy"));
        Assert.Contains(second, e => e.Type == "done");
    }

    [Fact]
    public async Task RetrievalUsesConfiguredMinScore_NotHardcoded03()
    {
        double? captured = null;
        var (orch, m) = Build(config: new Dictionary<string, string> { ["RETRIEVAL_MIN_SCORE"] = "0.7" }, setup: mm => {
            mm.Retrieval.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string __, int ___, double minScore, Dictionary<string, object> ____, CancellationToken _____) => captured = minScore)
                .Returns(Task.FromResult(new RetrievalResponse
                {
                    Results = new List<ChunkResult> { new ChunkResult { Score = 0.9 } }
                }));
        });

        await Collect(orch.StreamChat("sid", "msg", "tenant", "user", CancellationToken.None));

        Assert.Equal(0.7, captured);
    }

    [Fact]
    public async Task RetrievalError_ReturnsSafeMessage_NotRawError()
    {
        var (orch, m) = Build(m => {
            m.Retrieval.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("internal connection string secret"));
        });

        var events = await Collect(orch.StreamChat("sid", "msg", "tenant", "user", CancellationToken.None));

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Equal("Không thể truy xuất ngữ cảnh từ dịch vụ RAG. Vui lòng thử lại sau.", error.Error);
        Assert.DoesNotContain("internal connection string secret", error.Error);
        Assert.DoesNotContain("connection string", error.Error);
    }
}
