using RagAMuffin.Database;
using RagAMuffin.Models;
using RagAMuffin.Auth;
using RagAMuffin.Services.ExternalApps;
using RagAMuffin.Services.Interfaces;
using RagAMuffin.Services;
using RagAMuffin.Services.Connectors;
using RagAMuffin.Services.Extractors;
using RagAMuffin.Services.Logging;
using RagAMuffin.Qdrant;
using Qdrant.Client;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// In-memory log buffer — created before Build() so logger provider can share the same instance
var logBuffer = new InMemoryLogBuffer();
builder.Services.AddSingleton(logBuffer);

// SQLite — must be registered before any service that depends on it
builder.Services.AddSingleton<AppDatabase>();

// User profile — singleton so GmailConnector and setup endpoint share the same state
builder.Services.AddSingleton<UserProfileService>();

// Connector config — singleton so connectors and the config endpoint share the same state
builder.Services.AddSingleton<ConnectorConfigService>();

builder.Services.AddSingleton<SyncLogService>();
builder.Services.AddSingleton<NoteService>();
builder.Services.AddSingleton<BookmarkService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"]!);
});

builder.Services.AddHttpClient<ILlmService, OllamaLlmService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"]!);
    client.Timeout = TimeSpan.FromMinutes(10);
});

var qdrantHost = builder.Configuration["Qdrant:Host"] ?? "qdrant";
var qdrantPort = int.TryParse(builder.Configuration["Qdrant:Port"], out var configuredPort) ? configuredPort : 6334;

builder.Services.AddSingleton<QdrantClient>(sp =>
    new QdrantClient(qdrantHost, qdrantPort));

builder.Services.AddScoped<QdrantCollectionInitializer>();
builder.Services.AddScoped<IRagQueryService, RagQueryService>();
builder.Services.AddSingleton<ChatSessionService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddScoped<IVectorStore, QdrantVectorStore>();
builder.Services.AddScoped<IChunker>(sp => new TextChunker(sp.GetRequiredService<ILogger<TextChunker>>(), 100, 25));
builder.Services.AddScoped<IEmailParser, EmailParser>();
builder.Services.AddScoped<IIngestionPipeline, IngestionPipeline>();

// Named HttpClients for connectors that scrape the web
builder.Services.AddHttpClient("rss", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("web", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("RAG-A-Muffin/1.0");
});

// Connectors — add more here as new source types are implemented
builder.Services.AddScoped<IConnector, GmailConnector>();
builder.Services.AddScoped<IConnector, RssConnector>();
builder.Services.AddScoped<IConnector, WebConnector>();
builder.Services.AddScoped<IConnector, GoogleDriveConnector>();
builder.Services.AddScoped<IConnector, GoogleCalendarConnector>();
builder.Services.AddScoped<IConnector, LocalDirectoryConnector>();

// Document extractors — each handles a specific file extension
builder.Services.AddScoped<IDocumentExtractor, PdfExtractor>();
builder.Services.AddScoped<IDocumentExtractor, DocxExtractor>();
builder.Services.AddScoped<IDocumentExtractor, PlainTextExtractor>();
builder.Services.AddScoped<FileIngestionService>();

builder.Services.AddSingleton<ConnectorSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConnectorSyncService>());
builder.Services.AddHostedService<FileWatcherService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new InMemoryLoggerProvider(logBuffer));
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting Rag-A-Muffin application...");

Directory.CreateDirectory("/app/data/tokens");
Directory.CreateDirectory("/app/data/uploads");
Directory.CreateDirectory("/app/data/watch");

var initializer = app.Services.GetRequiredService<QdrantCollectionInitializer>();
await initializer.InitializeAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

app.MapGet("/authorize", async (HttpRequest request) =>
{
    var userId = request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest(new { Message = "Missing 'userId'.", Example = "/authorize?userId=you@example.com" });

    var redirectUri = $"{request.Scheme}://{request.Host}/oauth2callback";
    var authUrl = await GoogleAuth.GetAuthorizationUrlAsync(userId, redirectUri);
    return Results.Redirect(authUrl);
});

app.MapGet("/oauth2callback", async (HttpRequest request, string code, string? userId, string? state) =>
{
    var resolvedUserId = userId;
    if (string.IsNullOrWhiteSpace(resolvedUserId) && !string.IsNullOrWhiteSpace(state))
        resolvedUserId = Uri.UnescapeDataString(state);

    if (string.IsNullOrWhiteSpace(resolvedUserId))
        return Results.BadRequest(new { Message = "Missing userId.", Example = "/oauth2callback?code=...&userId=you@example.com" });

    var redirectUri = $"{request.Scheme}://{request.Host}/oauth2callback";
    await GoogleAuth.ExchangeCodeForTokenAsync(resolvedUserId, code, redirectUri);
    return Results.Text("Authentication complete. You may close this page.");
});

// ── Setup endpoints ──────────────────────────────────────────────────────────

app.MapGet("/setup/status", (UserProfileService profile) =>
    Results.Ok(new { isConfigured = profile.IsConfigured, userId = profile.UserId }));

app.MapPost("/setup", async (HttpRequest request, UserProfileService profile) =>
{
    var body = await request.ReadFromJsonAsync<SetupRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.Email))
        return Results.BadRequest(new { Message = "email is required." });

    await profile.SetUserIdAsync(body.Email);
    return Results.Ok(new { userId = body.Email });
});

// ── Log endpoint ─────────────────────────────────────────────────────────────

app.MapGet("/logs", (InMemoryLogBuffer buffer) => Results.Ok(buffer.GetAll()));

// ── System status endpoint ────────────────────────────────────────────────────

app.MapGet("/status", async (
    UserProfileService profile,
    ConnectorConfigService connectorConfig,
    IConfiguration config) =>
{
    var userConfigured = profile.IsConfigured;
    var googleAuthorized = false;
    if (userConfigured)
    {
        try { googleAuthorized = await GoogleAuth.HasStoredCredentialAsync(profile.UserId!); }
        catch { /* credentials file temporarily unavailable (e.g. during rebuild) */ }
    }

    return Results.Ok(new
    {
        user = new { configured = userConfigured, email = profile.UserId },
        googleAuthorized,
        rssFeeds     = connectorConfig.Current.RssFeeds.Count,
        webUrls      = connectorConfig.Current.WebUrls.Count,
        syncInterval = connectorConfig.Current.SyncIntervalMinutes > 0
            ? connectorConfig.Current.SyncIntervalMinutes
            : config.GetValue("Ingestion:IntervalMinutes", 60),
        enabledConnectors = connectorConfig.Current.EnabledConnectors,
        drive = new
        {
            folderCount = config.GetSection("Connectors:Drive:FolderIds").Get<string[]>()?.Length ?? 0,
            maxFiles    = config.GetValue("Connectors:Drive:MaxFiles", 50)
        },
        calendar = new
        {
            daysBack  = config.GetValue("Connectors:Calendar:DaysBack",  30),
            daysAhead = config.GetValue("Connectors:Calendar:DaysAhead", 7)
        }
    });
});

// ── App settings endpoints ───────────────────────────────────────────────────

app.MapGet("/config/settings",
    (SettingsService settings) => Results.Ok(settings.Current));

app.MapPut("/config/settings",
    async (AppSettings settings, SettingsService svc) =>
        Results.Ok(await svc.SaveAsync(settings)));

app.MapGet("/config/models", async (IConfiguration config, IHttpClientFactory factory) =>
{
    var baseUrl = config["Ollama:BaseUrl"] ?? "http://ollama:11434";
    try
    {
        using var http = factory.CreateClient();
        var res  = await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"{baseUrl}/api/tags");
        var names = res.GetProperty("models")
            .EnumerateArray()
            .Select(m => m.GetProperty("name").GetString())
            .Where(n => n is not null)
            .OrderBy(n => n)
            .ToList();
        return Results.Ok(names);
    }
    catch
    {
        return Results.Ok(new List<string>());
    }
});

// ── Connector config endpoints ────────────────────────────────────────────────

app.MapGet("/config/connectors",
    (ConnectorConfigService cfg) => Results.Ok(cfg.Current));

app.MapPut("/config/connectors",
    async (ConnectorConfig config, ConnectorConfigService cfg) =>
        Results.Ok(await cfg.SaveAsync(config)));

// Dev endpoint: triggers an immediate Gmail sync for the configured user
app.MapGet("/inbox", async (HttpRequest request, IIngestionPipeline pipeline, IEmailParser parser) =>
{
    var userId = request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest(new { Message = "Missing 'userId'.", Example = "/inbox?userId=you@example.com" });

    if (!await GoogleAuth.HasStoredCredentialAsync(userId))
    {
        var authUrl = $"{request.Scheme}://{request.Host}/authorize?userId={Uri.EscapeDataString(userId)}";
        return Results.BadRequest(new { Message = "No stored credentials.", AuthorizationUrl = authUrl });
    }

    var gmailService = await GoogleAuth.CreateGmailServiceAsync(userId);
    var messages = await Gmail.FetchInboxAsync(gmailService, maxResults: 10);

    logger.LogInformation("Fetched {Count} messages for {UserId}. Starting ingestion...", messages.Count, userId);

    var documents = messages
        .Select(m => parser.ParsedEmail(m))
        .Where(p => p is not null)
        .Select(p => new SourceDocument
        {
            Id          = p!.Id,
            SourceType  = "gmail",
            Title       = p.Subject,
            Author      = p.From,
            Recipient   = p.To,
            Cc          = p.Cc,
            Body        = p.Body,
            PublishedAt = p.Date,
            Metadata    = new Dictionary<string, string>
            {
                ["threadId"]       = p.ThreadId ?? string.Empty,
                ["labels"]         = p.Labels ?? string.Empty,
                ["hasAttachments"] = p.HasAttachments ? "true" : "false",
                ["direction"]      = p.Direction ?? "received"
            }
        })
        .ToList();

    await pipeline.IngestAsync(documents);

    return Results.Ok(documents.Select(d => new
    {
        id      = d.Id,
        title   = d.Title,
        author  = d.Author,
        preview = d.Body.Length > 100 ? d.Body[..100] + "..." : d.Body
    }));
});

app.MapPost("/ingest/upload", async (HttpRequest request, FileIngestionService ingestor, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { Message = "Expected multipart/form-data." });

    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null)
        return Results.BadRequest(new { Message = "No file field found in form data." });

    await using var stream = file.OpenReadStream();
    var id = await ingestor.IngestAsync(stream, file.FileName, ct);

    if (id is null)
        return Results.UnprocessableEntity(new { Message = "File could not be ingested. Unsupported format or empty content." });

    return Results.Ok(new { documentId = id, title = Path.GetFileNameWithoutExtension(file.FileName) });
}).DisableAntiforgery();

app.MapPost("/query", async (QueryRequest request, IRagQueryService queryService, CancellationToken ct) =>
{
    var response = await queryService.QueryAsync(request, ct);
    return Results.Ok(response);
});

app.MapPost("/query/stream", async (QueryRequest request, IRagQueryService queryService, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    await foreach (var token in queryService.StreamQueryAsync(request, ct))
    {
        if (token.StartsWith("[CITATIONS]:"))
        {
            await ctx.Response.WriteAsync($"event: citations\ndata: {token["[CITATIONS]:".Length..]}\n\n", ct);
        }
        else
        {
            await ctx.Response.WriteAsync($"data: {token}\n\n", ct);
        }
        await ctx.Response.Body.FlushAsync(ct);
    }

    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);
});

app.MapPost("/sync", async (ConnectorSyncService syncService, CancellationToken ct) =>
{
    await syncService.SyncAllAsync(ct);
    return Results.Ok(new { message = "Sync complete" });
});

// ── Chat session endpoints ────────────────────────────────────────────────────

app.MapGet("/chats", async (ChatSessionService chatService) =>
{
    var sessions = await chatService.ListAsync();
    return Results.Ok(sessions.Select(s => new
    {
        id           = s.Id,
        title        = s.Title,
        updatedAt    = s.UpdatedAt,
        messageCount = s.Messages.Count
    }));
});

app.MapGet("/chats/{id}", async (string id, ChatSessionService chatService) =>
{
    var session = await chatService.GetAsync(id);
    return session is null ? Results.NotFound() : Results.Ok(session);
});

app.MapPost("/chats", async (ChatSession session, ChatSessionService chatService) =>
    Results.Ok(await chatService.SaveAsync(session)));

app.MapDelete("/chats/{id}", async (string id, ChatSessionService chatService) =>
{
    await chatService.DeleteAsync(id);
    return Results.Ok(new { deleted = id });
});

app.MapGet("/chats/search", async (HttpRequest req, ChatSessionService chatService) =>
{
    var q = req.Query["q"].ToString();
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { message = "q is required" });
    var results = await chatService.SearchAsync(q);
    return Results.Ok(results.Select(s => new { s.Id, s.Title, s.UpdatedAt }));
});

// ── Index management endpoints ────────────────────────────────────────────────

app.MapGet("/index/stats", async (IVectorStore store, CancellationToken ct) =>
    Results.Ok(await store.GetStatsAsync(ct)));

app.MapGet("/index/documents", async (HttpRequest req, IVectorStore store, CancellationToken ct) =>
{
    var sourceType = req.Query["source"].ToString();
    var docs = await store.ListDocumentsAsync(
        string.IsNullOrWhiteSpace(sourceType) ? null : sourceType, ct);
    return Results.Ok(docs);
});

app.MapDelete("/index/documents/{documentId}", async (string documentId, IVectorStore store, CancellationToken ct) =>
{
    await store.DeleteByDocumentIdAsync(documentId, ct);
    logger.LogInformation("Document deleted from index: {DocumentId}", documentId);
    return Results.Ok(new { deleted = documentId });
});

app.MapDelete("/index/source/{sourceType}", async (string sourceType, IVectorStore store, CancellationToken ct) =>
{
    await store.DeleteBySourceTypeAsync(sourceType, ct);
    logger.LogInformation("All '{SourceType}' documents deleted from index", sourceType);
    return Results.Ok(new { deleted = sourceType });
});

app.MapPost("/index/documents/{documentId}/reindex", async (
    string documentId, IVectorStore store, IIngestionPipeline pipeline,
    IHttpClientFactory httpClientFactory, IEnumerable<IDocumentExtractor> extractors,
    CancellationToken ct) =>
{
    var chunk = await store.GetFirstChunkAsync(documentId, ct);
    if (chunk is null)
        return Results.NotFound(new { message = "Document not found in index." });

    logger.LogInformation("Re-indexing [{SourceType}] '{Title}' ({DocumentId})", chunk.SourceType, chunk.Title, documentId);

    switch (chunk.SourceType)
    {
        case "web":
        {
            if (string.IsNullOrWhiteSpace(chunk.Url))
                return Results.BadRequest(new { message = "Document has no URL — cannot re-fetch." });

            var client = httpClientFactory.CreateClient("web");
            string html;
            try { html = await client.GetStringAsync(chunk.Url, ct); }
            catch (Exception ex) { return Results.Problem($"Failed to fetch URL: {ex.Message}"); }

            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(html);
            var garbage = htmlDoc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header|//aside|//noscript");
            if (garbage != null)
                foreach (var node in garbage.ToList()) node.Remove();

            var titleNode = htmlDoc.DocumentNode.SelectSingleNode("//title");
            var pageTitle = HtmlAgilityPack.HtmlEntity.DeEntitize(titleNode?.InnerText?.Trim() ?? chunk.Title);
            var rawText   = htmlDoc.DocumentNode.InnerText;
            var text      = System.Text.RegularExpressions.Regex.Replace(rawText, @"[ \t]{2,}", " ");
            text          = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return Results.UnprocessableEntity(new { message = "Page returned no usable text." });

            await store.DeleteByDocumentIdAsync(documentId, ct);

            var host = Uri.TryCreate(chunk.Url, UriKind.Absolute, out var uri) ? uri.Host : chunk.Url;
            await pipeline.IngestAsync([new SourceDocument
            {
                Id          = documentId,
                SourceType  = "web",
                Title       = string.IsNullOrWhiteSpace(pageTitle) ? chunk.Title : pageTitle,
                Author      = host,
                Url         = chunk.Url,
                Body        = text,
                PublishedAt = DateTime.UtcNow,
                Metadata    = new Dictionary<string, string> { ["scrapedAt"] = DateTime.UtcNow.ToString("O") }
            }], ct);

            logger.LogInformation("Re-indexed web document: {Url}", chunk.Url);
            return Results.Ok(new { reindexed = documentId });
        }

        case "local":
        {
            var filePath = chunk.Metadata.GetValueOrDefault("filePath");
            if (string.IsNullOrWhiteSpace(filePath))
                return Results.BadRequest(new { message = "Document has no file path in metadata." });
            if (!File.Exists(filePath))
                return Results.BadRequest(new { message = $"File no longer exists at '{filePath}'." });

            var ext       = Path.GetExtension(filePath).ToLowerInvariant();
            var extractor = extractors.FirstOrDefault(e => e.CanHandle(ext));
            if (extractor is null)
                return Results.BadRequest(new { message = $"No extractor for '{ext}'." });

            string text;
            try
            {
                await using var stream = File.OpenRead(filePath);
                text = await extractor.ExtractAsync(stream, ct);
            }
            catch (Exception ex) { return Results.Problem($"Failed to read file: {ex.Message}"); }

            if (string.IsNullOrWhiteSpace(text))
                return Results.UnprocessableEntity(new { message = "File returned no usable text." });

            await store.DeleteByDocumentIdAsync(documentId, ct);

            await pipeline.IngestAsync([new SourceDocument
            {
                Id          = documentId,
                SourceType  = "local",
                Title       = chunk.Title,
                Author      = "local",
                Body        = text,
                PublishedAt = File.GetLastWriteTimeUtc(filePath),
                Metadata    = chunk.Metadata
            }], ct);

            logger.LogInformation("Re-indexed local document: {FilePath}", filePath);
            return Results.Ok(new { reindexed = documentId });
        }

        default:
            return Results.BadRequest(new { message = $"Re-index is not supported for '{chunk.SourceType}' documents. Use Sync All to refresh this source type." });
    }
});

// ── Retrieval preview ─────────────────────────────────────────────────────────

app.MapPost("/query/preview", async (QueryRequest request, IEmbeddingService embedder, IVectorStore store, CancellationToken ct) =>
{
    var vec    = await embedder.EmbedAsync(request.Query, ct);
    var chunks = await store.SearchAsync(vec, request.TopK, request.SourceTypes, request.DateFrom, request.DateTo, ct);
    return Results.Ok(chunks.Select(c => new
    {
        c.DocumentId, c.SourceType, c.Title, c.Author, c.PublishedAt, c.Score,
        preview = c.Text.Length > 300 ? c.Text[..300] + "…" : c.Text
    }));
});

// ── Chunk viewer ──────────────────────────────────────────────────────────────

app.MapGet("/index/documents/{documentId}/chunks", async (string documentId, IVectorStore store, CancellationToken ct) =>
{
    var chunks = await store.GetChunksAsync(documentId, ct);
    return Results.Ok(chunks.Select((c, i) => new { index = i, length = c.Text.Length, text = c.Text }));
});

// ── Find similar ──────────────────────────────────────────────────────────────

app.MapPost("/index/documents/{documentId}/similar", async (
    string documentId, IVectorStore store, IEmbeddingService embedder, CancellationToken ct) =>
{
    var seed = await store.GetFirstChunkAsync(documentId, ct);
    if (seed is null) return Results.NotFound(new { message = "Document not found." });
    var vec  = await embedder.EmbedAsync(seed.Text, ct);
    var hits = await store.SearchAsync(vec, 10, null, null, null, ct);
    return Results.Ok(hits.Where(h => h.DocumentId != documentId)
        .Select(h => new { h.DocumentId, h.SourceType, h.Title, h.Author, h.PublishedAt, h.Score }));
});

// ── Sync log endpoints ────────────────────────────────────────────────────────

app.MapGet("/synclog", (SyncLogService syncLog) => Results.Ok(syncLog.GetRecent(200)));
app.MapGet("/synclog/latest", (SyncLogService syncLog) => Results.Ok(syncLog.GetLatestPerConnector()));

// ── Notes endpoints ───────────────────────────────────────────────────────────

app.MapGet("/notes", (NoteService noteService) => Results.Ok(noteService.List()));

app.MapGet("/notes/{id}", (string id, NoteService noteService) =>
{
    var note = noteService.Get(id);
    return note is null ? Results.NotFound() : Results.Ok(note);
});

app.MapPost("/notes", async (Note note, NoteService noteService, IIngestionPipeline pipeline, CancellationToken ct) =>
{
    var saved = noteService.Save(note);
    await pipeline.IngestAsync([new SourceDocument
    {
        Id          = $"note-{saved.Id}",
        SourceType  = "note",
        Title       = saved.Title,
        Author      = "me",
        Body        = saved.Body,
        PublishedAt = saved.UpdatedAt,
        Metadata    = []
    }], ct);
    return Results.Ok(saved);
});

app.MapDelete("/notes/{id}", async (string id, NoteService noteService, IVectorStore store, CancellationToken ct) =>
{
    noteService.Delete(id);
    await store.DeleteByDocumentIdAsync($"note-{id}", ct);
    return Results.Ok(new { deleted = id });
});

// ── Bookmarks endpoints ───────────────────────────────────────────────────────

app.MapGet("/bookmarks", (BookmarkService bookmarkService) => Results.Ok(bookmarkService.List()));

app.MapPost("/bookmarks", (Bookmark bookmark, BookmarkService bookmarkService) =>
    Results.Ok(bookmarkService.Save(bookmark)));

app.MapDelete("/bookmarks/{id}", (string id, BookmarkService bookmarkService) =>
{
    bookmarkService.Delete(id);
    return Results.Ok(new { deleted = id });
});

// ── Dev / admin endpoints ─────────────────────────────────────────────────────

app.MapPost("/admin/restart", async ctx =>
{
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync("{\"message\":\"Restarting...\"}");
    await ctx.Response.CompleteAsync();
    _ = Task.Run(async () => { await Task.Delay(200); Environment.Exit(0); });
});

app.MapPost("/admin/rebuild", async ctx =>
{
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync("{\"message\":\"Rebuild started.\"}");
    await ctx.Response.CompleteAsync();

    var hostProjectDir = Environment.GetEnvironmentVariable("HOST_PROJECT_DIR") ?? "";
    var containerId = System.Net.Dns.GetHostName();

    _ = Task.Run(() =>
    {
        if (string.IsNullOrEmpty(hostProjectDir))
        {
            logger.LogError("Rebuild requires HOST_PROJECT_DIR env var — add it to docker-compose.yml");
            return;
        }

        // Disable restart policy so Docker doesn't race-restart us while the helper is building
        Process.Start(new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            ArgumentList = { "update", "--restart=no", containerId }
        })?.WaitForExit();

        // Start a detached helper container: it waits for this container to exit,
        // then performs the full rebuild in its own PID namespace (survives our exit)
        var script = $"docker wait {containerId} && docker compose -f /workspace/docker-compose.yml up --build -d api";
        var psi = new ProcessStartInfo("docker") { UseShellExecute = false };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--rm");
        psi.ArgumentList.Add("--detach");
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("/var/run/docker.sock:/var/run/docker.sock");
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add($"{hostProjectDir}:/workspace");
        // Pass the real host path so docker-compose picks it up via ${HOST_PROJECT_DIR:-${PWD}}
        // instead of evaluating ${PWD} inside the helper container (which would be /app).
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add($"HOST_PROJECT_DIR={hostProjectDir}");
        psi.ArgumentList.Add("--entrypoint"); psi.ArgumentList.Add("/bin/sh");
        psi.ArgumentList.Add("rag-a-muffin-api");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        Process.Start(psi)?.WaitForExit();

        Thread.Sleep(300);
        Environment.Exit(0);
    });
});

app.Run();

record SetupRequest(string Email);
