using RagAMuffin.Services.Interfaces;
using RagAMuffin.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RagAMuffin.Services
{
    public class RagQueryService : IRagQueryService
    {
        private readonly IEmbeddingService _embedder;
        private readonly IVectorStore _vectorStore;
        private readonly IFtsStore _ftsStore;
        private readonly ILlmService _llm;
        private readonly SettingsService _settings;
        private readonly ILogger<RagQueryService> _logger;

        public RagQueryService(
            IEmbeddingService embedder,
            IVectorStore vectorStore,
            IFtsStore ftsStore,
            ILlmService llm,
            SettingsService settings,
            ILogger<RagQueryService> logger)
        {
            _embedder = embedder;
            _vectorStore = vectorStore;
            _ftsStore = ftsStore;
            _llm = llm;
            _settings = settings;
            _logger = logger;
        }

        public async Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation("Embedding query: {Question}", request.Query);
            var queryVector = await _embedder.EmbedAsync(request.Query, ct);

            var chunks = await ResolveChunksAsync(request, request.Query, queryVector, ct);

            if (chunks.Count == 0)
            {
                return new QueryResponse
                {
                    Answer = "I couldn't find any relevant documents for your question.",
                    Citations = []
                };
            }

            var prompt = BuildPrompt(request.Query, chunks, request.History, _settings.Current.SystemPrompt);
            _logger.LogInformation("Sending prompt to LLM ({ChunkCount} chunks)...", chunks.Count);
            var answer = await _llm.CompleteAsync(prompt, ct);

            var citations = chunks.Select(c => new ChunkCitation
            {
                EmailId      = c.DocumentId,
                Subject      = c.Title,
                From         = c.Author,
                Date         = c.PublishedAt,
                RelevantText = c.Text
            }).ToList();

            return new QueryResponse { Answer = answer, Citations = citations };
        }

        public async IAsyncEnumerable<string> StreamQueryAsync(QueryRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _logger.LogInformation("Embedding streaming query: {Question}", request.Query);

            var noRetrieval = request.NoRetrieval || _settings.Current.NoRetrieval;
            List<ScoredChunk> chunks;

            if (noRetrieval)
            {
                _logger.LogInformation("No-retrieval mode active — skipping vector search");
                chunks = [];
            }
            else
            {
                // Preserve original query for FTS — BM25 needs the user's own words.
                // Query rewriting only improves the embedding, not keyword recall.
                var originalQuery = request.Query;
                var queryText     = request.Query;

                if (_settings.Current.QueryRewriting)
                {
                    var rewritePrompt = $"Rewrite the following question as a concise, keyword-rich search query. Output only the rewritten query, no explanation:\n{request.Query}";
                    var rewritten = (await _llm.CompleteAsync(rewritePrompt, ct)).Trim();
                    if (!string.IsNullOrWhiteSpace(rewritten))
                    {
                        _logger.LogInformation("Query rewritten: '{Original}' → '{Rewritten}'", request.Query, rewritten);
                        queryText = rewritten;
                    }
                }

                var queryVector = await _embedder.EmbedAsync(queryText, ct);
                chunks = await ResolveChunksAsync(request, originalQuery, queryVector, ct);

                foreach (var c in chunks)
                    _logger.LogInformation("Using: [{SourceType}] '{Title}' Score={Score:F3}",
                        c.SourceType, c.Title, c.Score);

                if (chunks.Count == 0)
                {
                    yield return "I couldn't find any relevant documents for your question.";
                    yield break;
                }
            }

            var prompt = BuildPrompt(request.Query, chunks, request.History, _settings.Current.SystemPrompt);

            await foreach (var token in _llm.StreamAsync(prompt, ct))
            {
                yield return token;
            }

            // One card per document — sort newest-first so recency queries surface the right result
            var seen = new HashSet<string>();
            var dedupedCitations = chunks
                .Where(c => seen.Add(c.DocumentId))
                .OrderByDescending(c => c.PublishedAt)
                .ThenByDescending(c => c.Score)
                .Select(c => new
                {
                    documentId     = c.DocumentId,
                    sourceType     = c.SourceType,
                    title          = c.Title,
                    author         = c.Author,
                    recipient      = c.Recipient,
                    date           = c.PublishedAt,
                    direction      = c.Metadata.GetValueOrDefault("direction"),
                    hasAttachments = c.Metadata.GetValueOrDefault("hasAttachments") == "true",
                    preview        = c.Text
                })
                .ToList();

            var citationsJson = JsonSerializer.Serialize(dedupedCitations);

            yield return $"[CITATIONS]:{citationsJson}";
        }

        private async Task<List<ScoredChunk>> ResolveChunksAsync(
            QueryRequest request, string originalQuery, float[] queryVector, CancellationToken ct)
        {
            // Retrieve a larger candidate pool from each source so RRF has more to work with.
            int candidateN = request.TopK * 3;

            var denseTask  = _vectorStore.SearchAsync(queryVector, candidateN, request.SourceTypes, request.DateFrom, request.DateTo, ct);
            var sparseTask = _ftsStore.SearchAsync(originalQuery, candidateN, request.SourceTypes, request.DateFrom, request.DateTo, ct);

            await Task.WhenAll(denseTask, sparseTask);
            _logger.LogInformation("Vector search: {Dense} chunks, FTS search: {Sparse} chunks",
                denseTask.Result.Count, sparseTask.Result.Count);

            var merged = RrfMerger.Merge(denseTask.Result, sparseTask.Result, request.TopK);

            // Person-filter (author/recipient exact match) still prepends high-confidence results.
            var (senderName, recipientName) = ExtractPersonFilters(originalQuery);

            if (senderName != null)
            {
                _logger.LogInformation("Sender filter detected: '{Name}' — searching 'author' field", senderName);
                var senderResults = await _vectorStore.SearchByFieldAsync("author", senderName, request.TopK, ct);
                _logger.LogInformation("Field search found {Count} author matches", senderResults.Count);
                merged = Merge(senderResults, merged, request.TopK);
            }

            if (recipientName != null)
            {
                _logger.LogInformation("Recipient filter detected: '{Name}' — searching 'recipient' field", recipientName);
                var recipientResults = await _vectorStore.SearchByFieldAsync("recipient", recipientName, request.TopK, ct);
                _logger.LogInformation("Field search found {Count} recipient matches", recipientResults.Count);
                merged = Merge(recipientResults, merged, request.TopK);
            }

            return merged;
        }

        // Payload-matched results go first (direct answer), then vector results fill remaining slots.
        private static List<ScoredChunk> Merge(List<ScoredChunk> primary, List<ScoredChunk> secondary, int limit)
        {
            var seenIds = primary.Select(c => c.DocumentId).ToHashSet();
            return primary
                .Concat(secondary.Where(c => !seenIds.Contains(c.DocumentId)))
                .Take(limit)
                .ToList();
        }

        private static (string? senderName, string? recipientName) ExtractPersonFilters(string query)
        {
            var fromMatch = Regex.Match(query,
                @"\bfrom\s+([A-Za-z][A-Za-z0-9\s\.]{1,40}?)(?:\s*\?|$|\s+(?:about|regarding|on|with|at|that|in))",
                RegexOptions.IgnoreCase);

            var toMatch = Regex.Match(query,
                @"\bto\s+([A-Za-z][A-Za-z0-9\s\.]{1,40}?)(?:\s*\?|$|\s+(?:about|regarding|on|with|at|that|in))",
                RegexOptions.IgnoreCase);

            return (
                fromMatch.Success ? fromMatch.Groups[1].Value.Trim() : null,
                toMatch.Success   ? toMatch.Groups[1].Value.Trim()   : null
            );
        }

        private static string BuildPrompt(string question, List<ScoredChunk> chunks, ChatMessage[]? history = null, string? customSystemPrompt = null)
        {
            var today = DateTime.Now.ToString("dddd, MMMM d, yyyy");

            var context = string.Join("\n\n", chunks.Select((c, i) =>
            {
                var raw  = c.ParentText ?? c.Text;
                var text = raw.Length > 1200 ? raw[..1200] + "…" : raw;
                var sb = new StringBuilder();

                // Source header varies by type
                var direction = c.Metadata.GetValueOrDefault("direction");
                var sourceLabel = c.SourceType == "gmail"
                    ? (direction == "sent" ? "Sent by you" : "Received")
                    : c.SourceType;

                sb.AppendLine($"[Document {i + 1}] {sourceLabel}");
                sb.AppendLine($"From: {c.Author}");
                if (!string.IsNullOrWhiteSpace(c.Recipient))
                    sb.AppendLine($"To: {c.Recipient}");
                if (!string.IsNullOrWhiteSpace(c.Url))
                    sb.AppendLine($"URL: {c.Url}");
                sb.AppendLine($"Title: {c.Title}");
                sb.AppendLine($"Date: {c.PublishedAt}");
                if (c.Metadata.GetValueOrDefault("hasAttachments") == "true")
                    sb.AppendLine("Has attachments: yes");
                sb.AppendLine($"Content: {text}");
                return sb.ToString().TrimEnd();
            }));

            var historySection = "";
            if (history is { Length: > 0 })
            {
                var turns = string.Join("\n", history.Select(m =>
                    m.Role == "user" ? $"User: {m.Content}" : $"Assistant: {m.Content}"));
                historySection = $"\n\nCONVERSATION HISTORY:\n{turns}";
            }

            var systemHeader = string.IsNullOrWhiteSpace(customSystemPrompt)
                ? $"You are a personal assistant. Today is {today}.\nAnswer the user's question using only the documents provided below.\nWhen a question refers to something from the conversation history, use both the documents and the history to answer.\nBe direct and specific — if the answer is yes/no, lead with that.\nWhen dates or authors matter, mention them in your answer.\nCite sources inline using the document numbers like [1] or [2] wherever you draw on them.\nIf the documents don't contain enough information to answer, say so clearly — don't guess."
                : customSystemPrompt.Replace("{today}", today);

            var documentsSection = chunks.Count > 0 ? $"\n\nDOCUMENTS:\n{context}" : "";

            return $"""
        {systemHeader}{documentsSection}{historySection}

        QUESTION: {question}

        ANSWER:
        """;
        }
    }
}
