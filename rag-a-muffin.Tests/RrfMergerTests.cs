using RagAMuffin.Models;
using RagAMuffin.Services;

namespace RagAMuffin.Tests;

public class RrfMergerTests
{
    private static ScoredChunk Chunk(string docId, int chunkIdx = 0) => new()
    {
        DocumentId  = docId,
        ChunkIndex  = chunkIdx,
        SourceType  = "test",
        Title       = docId,
        Author      = "test",
        PublishedAt = DateTime.UtcNow.ToString("O"),
        Text        = docId,
        Score       = 1.0f
    };

    [Fact]
    public void Merge_ChunkInBothLists_ScoresAreAdditive()
    {
        var dense  = new List<ScoredChunk> { Chunk("a"), Chunk("b") };
        var sparse = new List<ScoredChunk> { Chunk("a"), Chunk("c") };

        var result = RrfMerger.Merge(dense, sparse, topK: 3);

        // "a" appears in both lists — should score highest
        Assert.Equal("a", result[0].DocumentId);
    }

    [Fact]
    public void Merge_EmptySparseList_ReturnsDenseOnly()
    {
        var dense  = new List<ScoredChunk> { Chunk("a"), Chunk("b"), Chunk("c") };
        var sparse = new List<ScoredChunk>();

        var result = RrfMerger.Merge(dense, sparse, topK: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(["a", "b", "c"], result.Select(r => r.DocumentId));
    }

    [Fact]
    public void Merge_EmptyDenseList_ReturnsSparseOnly()
    {
        var dense  = new List<ScoredChunk>();
        var sparse = new List<ScoredChunk> { Chunk("x"), Chunk("y") };

        var result = RrfMerger.Merge(dense, sparse, topK: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(["x", "y"], result.Select(r => r.DocumentId));
    }

    [Fact]
    public void Merge_TopKLimitsOutput()
    {
        var dense  = new List<ScoredChunk> { Chunk("a"), Chunk("b"), Chunk("c"), Chunk("d") };
        var sparse = new List<ScoredChunk> { Chunk("e"), Chunk("f"), Chunk("g"), Chunk("h") };

        var result = RrfMerger.Merge(dense, sparse, topK: 3);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Merge_DifferentChunkIndexesSameDoc_TreatedAsDistinct()
    {
        var dense  = new List<ScoredChunk> { Chunk("doc1", 0), Chunk("doc1", 1) };
        var sparse = new List<ScoredChunk> { Chunk("doc1", 0) };

        var result = RrfMerger.Merge(dense, sparse, topK: 3);

        // chunk 0 of doc1 appears in both lists → highest score
        Assert.Equal(("doc1", 0), (result[0].DocumentId, result[0].ChunkIndex));
        // chunk 1 appears only in dense → also present
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Merge_ScoresArePositive()
    {
        var dense  = new List<ScoredChunk> { Chunk("a"), Chunk("b") };
        var sparse = new List<ScoredChunk> { Chunk("b"), Chunk("c") };

        var result = RrfMerger.Merge(dense, sparse, topK: 3);

        Assert.All(result, r => Assert.True(r.Score > 0));
    }

    [Fact]
    public void Merge_BothEmpty_ReturnsEmpty()
    {
        var result = RrfMerger.Merge([], [], topK: 5);
        Assert.Empty(result);
    }
}
