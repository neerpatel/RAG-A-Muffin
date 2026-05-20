namespace RagAMuffin.Models
{
    public class ScoredChunk
    {
        public required string DocumentId { get; init; }
        public required string SourceType { get; init; }
        public required string Title { get; init; }
        public required string Author { get; init; }
        public string? Recipient { get; init; }
        public string? Url { get; init; }
        public required string PublishedAt { get; init; }
        public required string Text { get; init; }
        public string? ParentText { get; init; }
        // Source-specific extras deserialized from Qdrant payload
        public Dictionary<string, string> Metadata { get; init; } = new();
        public required float Score { get; init; }
    }
}
