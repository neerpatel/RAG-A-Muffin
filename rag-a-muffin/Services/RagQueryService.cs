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
        private readonly ILlmService _llm;
        private readonly SettingsService _settings;
        private readonly ILogger<RagQueryService> _logger;

        public RagQueryService(
            IEmbeddingService embedder,
            IVectorStore vectorStore,
            ILlmService llm,
            SettingsService settings,
            ILogger<RagQueryService> logger)
        {
            _embedder = embedder;
            _vectorStore = vectorStore;
            _llm = llm;
            _settings = settings;
            _logger = logger;
        }

        public async Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation("Embedding query: {Question}", request.Query);
            var queryVector = await _embedder.EmbedAsync(request.Query, ct);

            var chunks = await ResolveChunksAsync(request, queryVector, ct);

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
                var queryVector = await _embedder.EmbedAsync(request.Query, ct);
                chunks = await ResolveChunksAsync(request, queryVector, ct);

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

        private async Task<List<ScoredChunk>> ResolveChunksAsync(QueryRequest request, float[] queryVector, CancellationToken ct)
        {
            var vectorResults = await _vectorStore.SearchAsync(queryVector, request.TopK, request.SourceTypes, request.DateFrom, request.DateTo, ct);
            _logger.LogInformation("Vector search returned {Count} chunks", vectorResults.Count);

            var (senderName, recipientName) = ExtractPersonFilters(request.Query);

            if (senderName != null)
            {
                _logger.LogInformation("Sender filter detected: '{Name}' — searching 'author' field", senderName);
                var senderResults = await _vectorStore.SearchByFieldAsync("author", senderName, request.TopK, ct);
                _logger.LogInformation("Field search found {Count} author matches", senderResults.Count);
                vectorResults = Merge(senderResults, vectorResults, request.TopK);
            }

            if (recipientName != null)
            {
                _logger.LogInformation("Recipient filter detected: '{Name}' — searching 'recipient' field", recipientName);
                var recipientResults = await _vectorStore.SearchByFieldAsync("recipient", recipientName, request.TopK, ct);
                _logger.LogInformation("Field search found {Count} recipient matches", recipientResults.Count);
                vectorResults = Merge(recipientResults, vectorResults, request.TopK);
            }

            return vectorResults;
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
                var text = c.Text.Length > 800 ? c.Text[..800] + "…" : c.Text;
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
                ? $"You are a personal assistant. Today is {today}.\nAnswer the user's question using only the documents provided below.\nWhen a question refers to something from the conversation history, use both the documents and the history to answer.\nBe direct and specific — if the answer is yes/no, lead with that.\nWhen dates or authors matter, mention them in your answer.\nIf the documents don't contain enough information to answer, say so clearly — don't guess."
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
