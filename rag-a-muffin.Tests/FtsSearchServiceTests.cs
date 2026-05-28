using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using RagAMuffin.Database;
using RagAMuffin.Models;
using RagAMuffin.Services;

namespace RagAMuffin.Tests;

public class FtsSearchServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly AppDatabase _db;
    private readonly FtsSearchService _svc;

    public FtsSearchServiceTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared;");
        _keepAlive.Open();

        _db  = new AppDatabase(NullLogger<AppDatabase>.Instance, "Data Source=:memory:;Mode=Memory;Cache=Shared;");
        _svc = new FtsSearchService(_db, NullLogger<FtsSearchService>.Instance);

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "DELETE FROM ChunksFTS";
        cmd.ExecuteNonQuery();
    }

    private static EmbeddedChunk MakeChunk(string docId, int idx, string sourceType, string text, DateTime? published = null) =>
        new()
        {
            DocumentId  = docId,
            ChunkIndex  = idx,
            SourceType  = sourceType,
            Title       = $"Doc {docId}",
            Author      = "test@example.com",
            PublishedAt = published ?? DateTime.UtcNow,
            Text        = text,
            TotalChunks = 1,
            Vector      = []
        };

    [Fact]
    public async Task UpsertAndSearch_ReturnsMatchingChunk()
    {
        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail", "kitchen renovation budget estimate"));

        var results = await _svc.SearchAsync("kitchen renovation", topN: 5);

        Assert.Single(results);
        Assert.Equal("doc1", results[0].DocumentId);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail", "quarterly earnings report"));

        var results = await _svc.SearchAsync("kitchen renovation", topN: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteByDocumentId_RemovesChunks()
    {
        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail", "project planning notes"));
        await _svc.UpsertAsync(MakeChunk("doc1", 1, "gmail", "project timeline details"));

        await _svc.DeleteByDocumentIdAsync("doc1");
        var results = await _svc.SearchAsync("project planning", topN: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteBySourceType_RemovesAllOfType()
    {
        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail",    "email about meetings"));
        await _svc.UpsertAsync(MakeChunk("doc2", 0, "rss",      "rss feed about meetings"));
        await _svc.UpsertAsync(MakeChunk("doc3", 0, "obsidian", "note about meetings"));

        await _svc.DeleteBySourceTypeAsync("gmail");
        var results = await _svc.SearchAsync("meetings", topN: 10);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.SourceType == "gmail");
    }

    [Fact]
    public async Task Search_SourceTypeFilter_OnlyReturnsMatchingType()
    {
        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail", "budget planning"));
        await _svc.UpsertAsync(MakeChunk("doc2", 0, "rss",   "budget planning"));

        var results = await _svc.SearchAsync("budget planning", topN: 5, sourceTypes: ["rss"]);

        Assert.Single(results);
        Assert.Equal("rss", results[0].SourceType);
    }

    [Fact]
    public async Task Upsert_SameChunkTwice_NosDuplicates()
    {
        var chunk = MakeChunk("doc1", 0, "gmail", "quarterly review notes");
        await _svc.UpsertAsync(chunk);
        await _svc.UpsertAsync(chunk);

        var results = await _svc.SearchAsync("quarterly review", topN: 5);
        Assert.Single(results);
    }

    [Fact]
    public async Task Count_ReflectsInsertedChunks()
    {
        Assert.Equal(0, await _svc.CountAsync());

        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail", "first chunk"));
        await _svc.UpsertAsync(MakeChunk("doc1", 1, "gmail", "second chunk"));

        Assert.Equal(2, await _svc.CountAsync());
    }

    [Fact]
    public async Task Search_InvalidFtsQuery_ReturnsEmptyGracefully()
    {
        await _svc.UpsertAsync(MakeChunk("doc1", 0, "gmail", "some content here"));

        // Quotes and special chars that could break raw FTS5 syntax
        var results = await _svc.SearchAsync("\"unclosed quote", topN: 5);

        // Should not throw — returns empty or partial results
        Assert.NotNull(results);
    }

    public void Dispose() => _keepAlive.Dispose();
}
