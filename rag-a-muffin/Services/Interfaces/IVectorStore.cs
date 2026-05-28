using RagAMuffin.Models;

namespace RagAMuffin.Services.Interfaces
{
    public interface IVectorStore
    {
        Task UpsertAsync(EmbeddedChunk chunk, CancellationToken ct = default);
        Task DeleteByDocumentIdAsync(string documentId, CancellationToken ct = default);
        Task DeleteBySourceTypeAsync(string sourceType, CancellationToken ct = default);
        Task<List<ScoredChunk>> SearchAsync(float[] queryVector, int topK = 5, IEnumerable<string>? sourceTypes = null, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null, CancellationToken ct = default);
        Task<List<ScoredChunk>> SearchByFieldAsync(string field, string value, int limit, CancellationToken ct = default);
        Task<bool> DocumentExistsAsync(string documentId, CancellationToken ct = default);
        Task<ScoredChunk?> GetFirstChunkAsync(string documentId, CancellationToken ct = default);
        Task<List<ScoredChunk>> GetChunksAsync(string documentId, CancellationToken ct = default);
        Task<List<DocumentSummary>> ListDocumentsAsync(string? sourceType = null, CancellationToken ct = default);
        Task<IndexStats> GetStatsAsync(CancellationToken ct = default);
        IAsyncEnumerable<ScoredChunk> ScrollAllChunksAsync(CancellationToken ct = default);
    }
}
