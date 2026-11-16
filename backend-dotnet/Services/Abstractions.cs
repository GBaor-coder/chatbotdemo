using BackendDotnet.Models;

namespace BackendDotnet.Services;

public interface ITenantContext
{
    TenantContext GetContext();
}

public interface IRetrievalClient
{
    Task<RetrievalResponse> SearchAsync(string query, string tenantId, int topK, double minScore, Dictionary<string, object> filters, CancellationToken ct);
}

public interface ILlmClient
{
    IAsyncEnumerable<string> StreamCompletionAsync(string prompt, CancellationToken ct);
}

public interface IGuardrail
{
    bool ShouldRefuse(List<ChunkResult> chunks, StructuredResult structured);
    string GetRefusalMessage();
}

public interface IPromptBuilder
{
    string Build(string query, List<ChunkResult> chunks, StructuredResult? structured);
}

public interface IStructuredQueryService
{
    Task<StructuredResult> QueryAsync(string question, string tenantId, CancellationToken ct);
}
