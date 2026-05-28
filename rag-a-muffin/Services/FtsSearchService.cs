using Microsoft.Data.Sqlite;
using RagAMuffin.Database;
using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;

namespace RagAMuffin.Services
{
    public class FtsSearchService : IFtsStore
    {
        private readonly AppDatabase _db;
        private readonly ILogger<FtsSearchService> _logger;

        public FtsSearchService(AppDatabase db, ILogger<FtsSearchService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task UpsertAsync(EmbeddedChunk chunk, CancellationToken ct = default)
        {
            using var conn = _db.Open();
            using var tx   = conn.BeginTransaction();

            // Delete existing entry for this chunk (handles re-sync)
            using var del = conn.CreateCommand();
            del.Transaction  = tx;
            del.CommandText  = "DELETE FROM ChunksFTS WHERE documentId = $id AND chunkIndex = $idx";
            del.Parameters.AddWithValue("$id",  chunk.DocumentId);
            del.Parameters.AddWithValue("$idx", chunk.ChunkIndex);
            await del.ExecuteNonQueryAsync(ct);

            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO ChunksFTS(text, documentId, chunkIndex, sourceType, publishedAt, title, author, parentText)
                VALUES ($text, $id, $idx, $sourceType, $publishedAt, $title, $author, $parentText)
                """;
            ins.Parameters.AddWithValue("$text",       chunk.Text);
            ins.Parameters.AddWithValue("$id",         chunk.DocumentId);
            ins.Parameters.AddWithValue("$idx",        chunk.ChunkIndex);
            ins.Parameters.AddWithValue("$sourceType", chunk.SourceType);
            ins.Parameters.AddWithValue("$publishedAt",chunk.PublishedAt.ToString("O"));
            ins.Parameters.AddWithValue("$title",      chunk.Title);
            ins.Parameters.AddWithValue("$author",     chunk.Author);
            ins.Parameters.AddWithValue("$parentText", (object?)chunk.ParentText ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync(ct);

            tx.Commit();
        }

        public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken ct = default)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ChunksFTS WHERE documentId = $id";
            cmd.Parameters.AddWithValue("$id", documentId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteBySourceTypeAsync(string sourceType, CancellationToken ct = default)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ChunksFTS WHERE sourceType = $type";
            cmd.Parameters.AddWithValue("$type", sourceType);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<ScoredChunk>> SearchAsync(
            string query,
            int topN,
            IEnumerable<string>? sourceTypes = null,
            DateTimeOffset? dateFrom = null,
            DateTimeOffset? dateTo = null,
            CancellationToken ct = default)
        {
            var ftsQuery = BuildFtsQuery(query);
            if (string.IsNullOrWhiteSpace(ftsQuery))
                return [];

            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();

            var sql = new System.Text.StringBuilder("""
                SELECT documentId, chunkIndex, sourceType, publishedAt, title, author, text, parentText
                FROM ChunksFTS
                WHERE ChunksFTS MATCH $query
                """);

            cmd.Parameters.AddWithValue("$query", ftsQuery);

            var types = sourceTypes?.ToList();
            if (types is { Count: > 0 })
            {
                sql.Append(" AND sourceType IN (");
                for (int i = 0; i < types.Count; i++)
                {
                    sql.Append(i > 0 ? ", " : "");
                    sql.Append($"$st{i}");
                    cmd.Parameters.AddWithValue($"$st{i}", types[i]);
                }
                sql.Append(')');
            }

            if (dateFrom.HasValue)
            {
                sql.Append(" AND publishedAt >= $dateFrom");
                cmd.Parameters.AddWithValue("$dateFrom", dateFrom.Value.ToString("O"));
            }

            if (dateTo.HasValue)
            {
                sql.Append(" AND publishedAt <= $dateTo");
                cmd.Parameters.AddWithValue("$dateTo", dateTo.Value.ToString("O"));
            }

            sql.Append(" ORDER BY rank LIMIT $n");
            cmd.Parameters.AddWithValue("$n", topN);

            cmd.CommandText = sql.ToString();

            var results = new List<ScoredChunk>();
            try
            {
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    results.Add(new ScoredChunk
                    {
                        DocumentId  = reader.GetString(0),
                        ChunkIndex  = reader.GetInt32(1),
                        SourceType  = reader.GetString(2),
                        PublishedAt = reader.GetString(3),
                        Title       = reader.GetString(4),
                        Author      = reader.GetString(5),
                        Text        = reader.GetString(6),
                        ParentText  = reader.IsDBNull(7) ? null : reader.GetString(7),
                        Score       = 1.0f   // rank order is what matters for RRF
                    });
                }
            }
            catch (SqliteException ex)
            {
                // Invalid FTS5 query syntax — log and return empty rather than crashing
                _logger.LogWarning(ex, "FTS5 query failed for input '{Query}', returning empty results", query);
            }

            _logger.LogInformation("FTS search returned {Count} chunks for query '{Query}'", results.Count, query);
            return results;
        }

        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM ChunksFTS";
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is long l ? l : Convert.ToInt64(result);
        }

        // Strips FTS5 special characters and builds a safe query.
        // Each word is quoted to handle punctuation; short words are kept
        // (the porter stemmer handles common stopwords naturally).
        private static string BuildFtsQuery(string raw)
        {
            var words = raw
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.Replace("\"", "").Replace("*", "").Replace("^", "").Trim())
                .Where(w => w.Length > 1);

            return string.Join(" ", words.Select(w => $"\"{w}\""));
        }
    }
}
