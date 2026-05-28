using RagAMuffin.Models;

namespace RagAMuffin.Services
{
    public static class RrfMerger
    {
        // k=60 is the standard RRF constant from the original paper (Cormack et al. 2009).
        // It smooths out the impact of high-rank outliers and is robust across domains.
        public static List<ScoredChunk> Merge(
            List<ScoredChunk> dense,
            List<ScoredChunk> sparse,
            int topK,
            int k = 60)
        {
            var scores = new Dictionary<(string docId, int chunkIdx), (float score, ScoredChunk chunk)>();

            void Accumulate(List<ScoredChunk> ranked)
            {
                for (int i = 0; i < ranked.Count; i++)
                {
                    var chunk = ranked[i];
                    var key   = (chunk.DocumentId, chunk.ChunkIndex);
                    var rrf   = 1.0f / (k + i + 1);

                    if (scores.TryGetValue(key, out var existing))
                        scores[key] = (existing.score + rrf, existing.chunk);
                    else
                        scores[key] = (rrf, chunk);
                }
            }

            Accumulate(dense);
            Accumulate(sparse);

            return scores.Values
                .OrderByDescending(v => v.score)
                .Take(topK)
                .Select(v => new ScoredChunk
                {
                    DocumentId  = v.chunk.DocumentId,
                    ChunkIndex  = v.chunk.ChunkIndex,
                    SourceType  = v.chunk.SourceType,
                    Title       = v.chunk.Title,
                    Author      = v.chunk.Author,
                    Recipient   = v.chunk.Recipient,
                    Url         = v.chunk.Url,
                    PublishedAt = v.chunk.PublishedAt,
                    Text        = v.chunk.Text,
                    ParentText  = v.chunk.ParentText,
                    Metadata    = v.chunk.Metadata,
                    Score       = v.score
                })
                .ToList();
        }
    }
}
