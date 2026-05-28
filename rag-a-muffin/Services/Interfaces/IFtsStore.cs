using RagAMuffin.Models;

namespace RagAMuffin.Services.Interfaces
{
    public interface IFtsStore
    {
        Task UpsertAsync(EmbeddedChunk chunk, CancellationToken ct = default);
        Task DeleteByDocumentIdAsync(string documentId, CancellationToken ct = default);
        Task DeleteBySourceTypeAsync(string sourceType, CancellationToken ct = default);
        Task<List<ScoredChunk>> SearchAsync(
            string query,
            int topN,
            IEnumerable<string>? sourceTypes = null,
            DateTimeOffset? dateFrom = null,
            DateTimeOffset? dateTo = null,
            CancellationToken ct = default);
        Task<long> CountAsync(CancellationToken ct = default);
    }
}
