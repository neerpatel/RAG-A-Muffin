using RagAMuffin.Services.Interfaces;
using RagAMuffin.Models;
using System.Text;

namespace RagAMuffin.Services
{
    public class IngestionPipeline : IIngestionPipeline
    {
        private readonly ILogger<IngestionPipeline> _logger;
        private readonly IChunker _chunker;
        private readonly IEmbeddingService _embedder;
        private readonly IVectorStore _vectorStore;

        public IngestionPipeline(
            ILogger<IngestionPipeline> logger,
            IChunker chunker,
            IEmbeddingService embedder,
            IVectorStore vectorStore)
        {
            _logger = logger;
            _chunker = chunker;
            _embedder = embedder;
            _vectorStore = vectorStore;
        }

        public async Task IngestAsync(IEnumerable<SourceDocument> documents, CancellationToken ct = default)
        {
            foreach (var doc in documents)
            {
                _logger.LogInformation("Processing [{SourceType}] '{Title}'", doc.SourceType, doc.Title);

                if (await _vectorStore.DocumentExistsAsync(doc.Id, ct))
                {
                    _logger.LogInformation("Document {Id} already ingested, skipping", doc.Id);
                    continue;
                }

                var chunks = _chunker.Chunk(doc);
                if (chunks.Count == 0)
                {
                    _logger.LogInformation("Skipping '{Title}' — empty after chunking", doc.Title);
                    continue;
                }

                foreach (var chunk in chunks)
                {
                    float[] vector;
                    try
                    {
                        vector = await _embedder.EmbedAsync(BuildEmbedText(doc, chunk.Text), ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to embed chunk {ChunkIndex} for '{Title}'. Skipping.", chunk.Index, doc.Title);
                        continue;
                    }

                    await _vectorStore.UpsertAsync(new EmbeddedChunk
                    {
                        DocumentId  = doc.Id,
                        SourceType  = doc.SourceType,
                        Title       = doc.Title,
                        Author      = doc.Author,
                        Recipient   = doc.Recipient,
                        Cc          = doc.Cc,
                        Url         = doc.Url,
                        PublishedAt = doc.PublishedAt,
                        Metadata    = doc.Metadata,
                        ChunkIndex  = chunk.Index,
                        TotalChunks = chunk.TotalChunks,
                        Text        = chunk.Text,
                        ParentText  = chunk.ParentText,
                        Vector      = vector
                    }, ct);
                }
            }
        }

        private static string BuildEmbedText(SourceDocument doc, string chunkText)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"From: {doc.Author}");
            if (!string.IsNullOrWhiteSpace(doc.Recipient))
                sb.AppendLine($"To: {doc.Recipient}");
            if (!string.IsNullOrWhiteSpace(doc.Cc))
                sb.AppendLine($"Cc: {doc.Cc}");
            sb.AppendLine($"Subject: {doc.Title}");
            if (doc.Metadata.TryGetValue("labels", out var labels) && !string.IsNullOrWhiteSpace(labels))
                sb.AppendLine($"Labels: {labels}");
            if (doc.Metadata.TryGetValue("hasAttachments", out var att) && att == "true")
                sb.AppendLine("Has attachments: yes");
            sb.AppendLine();
            sb.Append(chunkText);
            return sb.ToString();
        }
    }
}
