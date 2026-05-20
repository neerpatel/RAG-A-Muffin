namespace RagAMuffin.Models
{
    public class EmbeddedChunk
    {
        public required string Text { get; init; }
        public string? ParentText { get; init; }
        public required int ChunkIndex { get; init; }
        public required int TotalChunks { get; init; }

        public required string DocumentId { get; init; }
        public required string SourceType { get; init; }
        public required string Title { get; init; }
        public required string Author { get; init; }
        public string? Recipient { get; init; }
        public string? Cc { get; init; }
        public string? Url { get; init; }
        public required DateTime PublishedAt { get; init; }
        // Source-specific extras serialized as JSON in Qdrant payload
        public Dictionary<string, string> Metadata { get; init; } = new();

        public required float[] Vector { get; init; }
    }
}
