using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using RagAMuffin.Database;
using RagAMuffin.Models;
using RagAMuffin.Services;

namespace RagAMuffin.Tests;

// Uses an in-memory SQLite database — no file I/O, no Docker, runs offline.
public class ChatSessionServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly AppDatabase _db;
    private readonly ChatSessionService _svc;

    public ChatSessionServiceTests()
    {
        // Shared-cache in-memory DB: keep a connection open so the schema persists
        _keepAlive = new SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared;");
        _keepAlive.Open();

        _db  = new AppDatabase(NullLogger<AppDatabase>.Instance, "Data Source=:memory:;Mode=Memory;Cache=Shared;");
        _svc = new ChatSessionService(_db, NullLogger<ChatSessionService>.Instance);
    }

    [Fact]
    public async Task SaveAndList_RoundTrip()
    {
        var session = new ChatSession
        {
            Id        = Guid.NewGuid().ToString(),
            Title     = "Test session",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Messages  = [new ChatMessage { Role = "user", Content = "Hello" }]
        };

        await _svc.SaveAsync(session);
        var list = await _svc.ListAsync();

        Assert.Contains(list, s => s.Id == session.Id && s.Title == "Test session");
    }

    [Fact]
    public async Task GetAsync_ReturnsMessages()
    {
        var session = new ChatSession
        {
            Id        = Guid.NewGuid().ToString(),
            Title     = "With messages",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Messages  =
            [
                new ChatMessage { Role = "user",      Content = "Hi" },
                new ChatMessage { Role = "assistant", Content = "Hello!" }
            ]
        };

        await _svc.SaveAsync(session);
        var loaded = await _svc.GetAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Messages.Count);
        Assert.Equal("Hi",     loaded.Messages[0].Content);
        Assert.Equal("Hello!", loaded.Messages[1].Content);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSession()
    {
        var id = Guid.NewGuid().ToString();
        await _svc.SaveAsync(new ChatSession
        {
            Id = id, Title = "To delete",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Messages = []
        });

        await _svc.DeleteAsync(id);

        var loaded = await _svc.GetAsync(id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SearchAsync_FindsByTitle()
    {
        var id = Guid.NewGuid().ToString();
        await _svc.SaveAsync(new ChatSession
        {
            Id = id, Title = "Quarterly review discussion",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Messages = []
        });

        var results = await _svc.SearchAsync("quarterly");
        Assert.Contains(results, s => s.Id == id);
    }

    [Fact]
    public async Task SearchAsync_FindsByMessageContent()
    {
        var id = Guid.NewGuid().ToString();
        await _svc.SaveAsync(new ChatSession
        {
            Id = id, Title = "Random title",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Messages  = [new ChatMessage { Role = "user", Content = "Tell me about embeddings" }]
        });

        var results = await _svc.SearchAsync("embeddings");
        Assert.Contains(results, s => s.Id == id);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
