namespace RagAMuffin.Models
{
    public class QueryRequest
    {
        public required string Query { get; init; }
        public int TopK { get; init; } = 8;
        public string[]?      SourceTypes  { get; init; }
        public ChatMessage[]? History      { get; init; }
        public DateTimeOffset? DateFrom    { get; init; }
        public DateTimeOffset? DateTo      { get; init; }
        public bool            NoRetrieval { get; init; } = false;
    }
}
