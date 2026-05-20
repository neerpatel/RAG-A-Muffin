using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;

namespace RagAMuffin.Services
{
    public class TextChunker : IChunker
    {
        private readonly int _chunkSize;
        private readonly int _overlap;
        private readonly ILogger<TextChunker> _logger;

        public TextChunker(ILogger<TextChunker> logger, int chunkSize = 200, int overlap = 50)
        {
            _logger = logger;
            _chunkSize = chunkSize;
            _overlap = overlap;
        }

        public List<TextChunk> Chunk(SourceDocument document)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.Body))
            {
                _logger.LogWarning("Chunk called with null or empty body, returning empty list");
                return [];
            }

            var words = document.Body.Split(' ');

            if (words.Length <= _chunkSize)
            {
                return
                [
                    new TextChunk
                    {
                        Text = document.Body,
                        Index = 0,
                        TotalChunks = 1,
                        CharStart = 0,
                        CharEnd = document.Body.Length
                    }
                ];
            }

            var totalChunks = Math.Max(1,
                (int)Math.Ceiling((double)(words.Length - _overlap) / (_chunkSize - _overlap)));

            var chunks = new List<TextChunk>(totalChunks);
            var index = 0;
            var position = 0;
            var parentPadding = _chunkSize / 2;

            while (position < words.Length)
            {
                var slice = words.Skip(position).Take(_chunkSize).ToArray();
                var text = string.Join(' ', slice);

                var parentStart = Math.Max(0, position - parentPadding);
                var parentEnd   = Math.Min(words.Length, position + _chunkSize + parentPadding);
                var parentSlice = words[parentStart..parentEnd];
                var parentText  = parentSlice.Length > slice.Length
                    ? string.Join(' ', parentSlice)
                    : null; // no point storing if identical to chunk

                chunks.Add(new TextChunk
                {
                    Text       = text,
                    ParentText = parentText,
                    Index      = index++,
                    TotalChunks = totalChunks,
                    CharStart  = position,
                    CharEnd    = position + slice.Length
                });

                position += _chunkSize - _overlap;
            }

            _logger.LogInformation("Chunked '{Title}' into {ChunkCount} chunks ({ChunkSize} words, {Overlap} overlap).",
                document.Title, chunks.Count, _chunkSize, _overlap);

            return chunks;
        }
    }
}
