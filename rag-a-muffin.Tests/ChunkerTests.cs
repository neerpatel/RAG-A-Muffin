using Microsoft.Extensions.Logging.Abstractions;
using RagAMuffin.Models;
using RagAMuffin.Services;

namespace RagAMuffin.Tests;

public class ChunkerTests
{
    private static TextChunker Make(int size = 10, int overlap = 2) =>
        new(NullLogger<TextChunker>.Instance, size, overlap);

    private static SourceDocument Doc(string body) => new()
    {
        Id = "test", SourceType = "test", Title = "T", Author = "A",
        Body = body, PublishedAt = DateTime.UtcNow
    };

    [Fact]
    public void ShortDocument_ProducesSingleChunk()
    {
        var chunker = Make(size: 100);
        var chunks  = chunker.Chunk(Doc("hello world"));
        Assert.Single(chunks);
        Assert.Equal("hello world", chunks[0].Text);
    }

    [Fact]
    public void NullBody_ReturnsEmpty()
    {
        var chunker = Make();
        var result  = chunker.Chunk(new SourceDocument
        {
            Id = "x", SourceType = "x", Title = "x", Author = "x",
            Body = "", PublishedAt = DateTime.UtcNow
        });
        Assert.Empty(result);
    }

    [Fact]
    public void LongDocument_ProducesMultipleChunks()
    {
        var words   = string.Join(' ', Enumerable.Range(1, 50).Select(i => $"word{i}"));
        var chunker = Make(size: 10, overlap: 2);
        var chunks  = chunker.Chunk(Doc(words));
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ChunkIndices_AreSequential()
    {
        var words   = string.Join(' ', Enumerable.Range(1, 50).Select(i => $"w{i}"));
        var chunker = Make(size: 10, overlap: 2);
        var chunks  = chunker.Chunk(Doc(words));
        for (var i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].Index);
    }

    [Fact]
    public void TotalChunks_IsConsistentAcrossAllChunks()
    {
        var words  = string.Join(' ', Enumerable.Range(1, 50).Select(i => $"w{i}"));
        var chunks = Make(size: 10, overlap: 2).Chunk(Doc(words));
        var total  = chunks[0].TotalChunks;
        Assert.All(chunks, c => Assert.Equal(total, c.TotalChunks));
    }

    [Fact]
    public void ParentText_IsLargerThanChunkText_ForMiddleChunks()
    {
        var words  = string.Join(' ', Enumerable.Range(1, 100).Select(i => $"w{i}"));
        var chunks = Make(size: 10, overlap: 2).Chunk(Doc(words));
        // Middle chunks should have parent context on both sides
        var middle = chunks[chunks.Count / 2];
        Assert.NotNull(middle.ParentText);
        Assert.True(middle.ParentText!.Split(' ').Length > middle.Text.Split(' ').Length);
    }

    [Fact]
    public void SingleChunkDocument_HasNullParentText()
    {
        var chunk = Make(size: 100).Chunk(Doc("one two three"))[0];
        Assert.Null(chunk.ParentText);
    }
}
