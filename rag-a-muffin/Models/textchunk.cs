namespace RagAMuffin.Models
{
    public class TextChunk
    {
        // The actual text content that gets embedded
        public required string Text { get; init; }

        // Position within the email — useful for reconstructing order
        // and for debugging ("chunk 3 of 7")
        public required int Index { get; init; }

        // Total chunks this email was split into
        // Lets you know at retrieval time if you're missing siblings
        public required int TotalChunks { get; init; }

        // Character offsets into the original body — helpful for
        // highlighting source text in a UI later
        public int CharStart { get; init; }
        public int CharEnd { get; init; }

        // Larger surrounding window sent to the LLM for context; null means use Text
        public string? ParentText { get; init; }
    }
}