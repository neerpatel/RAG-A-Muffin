using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;
using Google.Protobuf.Collections;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.Text.Json;

namespace RagAMuffin.Services
{
    public class QdrantVectorStore : IVectorStore
    {
        private readonly QdrantClient _client;
        private const string CollectionName = "documents";
        private readonly ILogger<QdrantVectorStore> _logger;

        public QdrantVectorStore(ILogger<QdrantVectorStore> logger, QdrantClient client)
        {
            _logger = logger;
            _client = client;
        }

        public async Task UpsertAsync(EmbeddedChunk chunk, CancellationToken ct = default)
        {
            var point = new PointStruct
            {
                Id = Guid.NewGuid(),
                Vectors = chunk.Vector,
                Payload =
                {
                    ["documentId"]  = chunk.DocumentId,
                    ["sourceType"]  = chunk.SourceType,
                    ["title"]       = chunk.Title,
                    ["author"]      = chunk.Author,
                    ["recipient"]   = chunk.Recipient ?? string.Empty,
                    ["cc"]          = chunk.Cc ?? string.Empty,
                    ["url"]         = chunk.Url ?? string.Empty,
                    ["publishedAt"] = chunk.PublishedAt.ToString("O"),
                    ["chunkIndex"]  = chunk.ChunkIndex,
                    ["totalChunks"] = chunk.TotalChunks,
                    ["text"]        = chunk.Text,
                    ["metadata"]    = JsonSerializer.Serialize(chunk.Metadata)
                }
            };

            _logger.LogInformation("Upserting [{SourceType}] '{Title}' chunk {ChunkIndex}",
                chunk.SourceType, chunk.Title, chunk.ChunkIndex);
            await _client.UpsertAsync(CollectionName, [point], cancellationToken: ct);
        }

        public async Task<List<ScoredChunk>> SearchAsync(float[] queryVector, int topK = 5, IEnumerable<string>? sourceTypes = null, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null, CancellationToken ct = default)
        {
            var types = sourceTypes?.ToList();
            Filter? filter = null;

            if (types is { Count: > 0 })
            {
                filter = new Filter();
                foreach (var st in types)
                    filter.Should.Add(new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key   = "sourceType",
                            Match = new Match { Keyword = st }
                        }
                    });
            }

            if (dateFrom.HasValue || dateTo.HasValue)
            {
                filter ??= new Filter();
                var range = new DatetimeRange();
                if (dateFrom.HasValue) range.Gte = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(dateFrom.Value);
                if (dateTo.HasValue)   range.Lte = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(dateTo.Value);
                filter.Must.Add(new Condition
                {
                    Field = new FieldCondition { Key = "publishedAt", DatetimeRange = range }
                });
            }

            var results = await _client.SearchAsync(
                CollectionName,
                queryVector,
                filter: filter,
                limit: (ulong)topK,
                cancellationToken: ct
            );

            return results.Select(r => MapPayload(r.Payload, r.Score)).ToList();
        }

        public async Task<List<ScoredChunk>> SearchByFieldAsync(string field, string value, int limit, CancellationToken ct = default)
        {
            var results = await _client.SearchAsync(
                CollectionName,
                new float[768],
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key   = field,
                                Match = new Match { Text = value }
                            }
                        }
                    }
                },
                limit: (ulong)limit,
                cancellationToken: ct
            );

            return results.Select(r => MapPayload(r.Payload, 1.0f)).ToList();
        }

        public async Task<bool> DocumentExistsAsync(string documentId, CancellationToken ct = default)
        {
            var results = await _client.SearchAsync(
                CollectionName,
                new float[768],
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key   = "documentId",
                                Match = new Match { Keyword = documentId }
                            }
                        }
                    }
                },
                limit: 1,
                cancellationToken: ct
            );

            return results.Any();
        }

        public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken ct = default)
        {
            await _client.DeleteAsync(CollectionName, new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key   = "documentId",
                            Match = new Match { Keyword = documentId }
                        }
                    }
                }
            }, cancellationToken: ct);
        }

        public async Task DeleteBySourceTypeAsync(string sourceType, CancellationToken ct = default)
        {
            await _client.DeleteAsync(CollectionName, new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key   = "sourceType",
                            Match = new Match { Keyword = sourceType }
                        }
                    }
                }
            }, cancellationToken: ct);
        }

        public async Task<ScoredChunk?> GetFirstChunkAsync(string documentId, CancellationToken ct = default)
        {
            var result = await _client.ScrollAsync(
                CollectionName,
                new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key   = "documentId",
                                Match = new Match { Keyword = documentId }
                            }
                        }
                    }
                },
                1,
                null,
                (WithPayloadSelector)true,
                null,
                null,
                null,
                null,
                ct);

            var point = result.Result.FirstOrDefault();
            return point is null ? null : MapPayload(point.Payload, 0f);
        }

        public async Task<List<ScoredChunk>> GetChunksAsync(string documentId, CancellationToken ct = default)
        {
            var chunks  = new List<ScoredChunk>();
            PointId?    offset    = null;
            const uint  batchSize = 100;
            var filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key   = "documentId",
                            Match = new Match { Keyword = documentId }
                        }
                    }
                }
            };

            do
            {
                var scrollResult = await _client.ScrollAsync(
                    CollectionName, filter, batchSize, offset,
                    (WithPayloadSelector)true, null, null, null, null, ct);

                chunks.AddRange(scrollResult.Result.Select(p => MapPayload(p.Payload, 0f)));
                offset = scrollResult.NextPageOffset;
            }
            while (offset is not null);

            return chunks.OrderBy(c => c.Metadata.GetValueOrDefault("chunkIndex")).ToList();
        }

        public async Task<List<DocumentSummary>> ListDocumentsAsync(string? sourceType = null, CancellationToken ct = default)
        {
            var seen   = new Dictionary<string, DocumentSummary>(StringComparer.Ordinal);
            PointId?   offset = null;
            const uint batchSize = 500;

            Filter? filter = sourceType is null ? null : new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key   = "sourceType",
                            Match = new Match { Keyword = sourceType }
                        }
                    }
                }
            };

            do
            {
                var scrollResult = await _client.ScrollAsync(
                    CollectionName,
                    filter,
                    batchSize,
                    offset,
                    (WithPayloadSelector)true,
                    null,
                    null,
                    null,
                    null,
                    ct);

                foreach (var point in scrollResult.Result)
                {
                    var docId = point.Payload["documentId"].StringValue;
                    if (seen.ContainsKey(docId)) continue;

                    var totalChunks = 1;
                    if (point.Payload.TryGetValue("totalChunks", out var tc))
                        totalChunks = (int)tc.IntegerValue;

                    var url = point.Payload.TryGetValue("url", out var u) && !string.IsNullOrEmpty(u.StringValue)
                        ? u.StringValue : null;

                    seen[docId] = new DocumentSummary
                    {
                        DocumentId  = docId,
                        SourceType  = point.Payload["sourceType"].StringValue,
                        Title       = point.Payload["title"].StringValue,
                        Author      = point.Payload["author"].StringValue,
                        Url         = url,
                        PublishedAt = point.Payload["publishedAt"].StringValue,
                        ChunkCount  = totalChunks
                    };
                }

                offset = scrollResult.NextPageOffset;
            }
            while (offset is not null);

            return seen.Values
                .OrderByDescending(d => d.PublishedAt)
                .ToList();
        }

        public async Task<IndexStats> GetStatsAsync(CancellationToken ct = default)
        {
            var docs = await ListDocumentsAsync(null, ct);
            var byType = docs
                .GroupBy(d => d.SourceType)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            return new IndexStats
            {
                TotalVectors = docs.Sum(d => (long)d.ChunkCount),
                BySourceType = byType
            };
        }

        private static ScoredChunk MapPayload(MapField<string, Value> payload, float score)
        {
            var metadataRaw = payload.TryGetValue("metadata", out var m) ? m.StringValue : "{}";
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataRaw) ?? new();

            return new ScoredChunk
            {
                DocumentId  = payload["documentId"].StringValue,
                SourceType  = payload["sourceType"].StringValue,
                Title       = payload["title"].StringValue,
                Author      = payload["author"].StringValue,
                Recipient   = payload.TryGetValue("recipient", out var r) ? r.StringValue : null,
                Url         = payload.TryGetValue("url", out var u) && !string.IsNullOrEmpty(u.StringValue) ? u.StringValue : null,
                PublishedAt = payload["publishedAt"].StringValue,
                Text        = payload["text"].StringValue,
                Metadata    = metadata,
                Score       = score
            };
        }
    }
}
