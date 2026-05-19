using RagAMuffin.Database;

namespace RagAMuffin.Services
{
    public class SyncLogEntry
    {
        public long     Id            { get; set; }
        public string   ConnectorName { get; set; } = string.Empty;
        public DateTime RanAt         { get; set; }
        public string   Outcome       { get; set; } = string.Empty; // "ok" | "error"
        public int      DocumentCount { get; set; }
        public string?  ErrorMessage  { get; set; }
    }

    public class SyncLogService
    {
        private readonly AppDatabase _db;

        public SyncLogService(AppDatabase db) => _db = db;

        public void Record(string connectorName, string outcome, int documentCount, string? errorMessage = null)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SyncLog (ConnectorName, RanAt, Outcome, DocumentCount, ErrorMessage)
                VALUES ($name, datetime('now'), $outcome, $count, $err)
                """;
            cmd.Parameters.AddWithValue("$name",    connectorName);
            cmd.Parameters.AddWithValue("$outcome", outcome);
            cmd.Parameters.AddWithValue("$count",   documentCount);
            cmd.Parameters.AddWithValue("$err",     (object?)errorMessage ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public List<SyncLogEntry> GetRecent(int limit = 100)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ConnectorName, RanAt, Outcome, DocumentCount, ErrorMessage
                FROM   SyncLog
                ORDER  BY Id DESC
                LIMIT  $limit
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            var entries = new List<SyncLogEntry>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new SyncLogEntry
                {
                    Id            = reader.GetInt64(0),
                    ConnectorName = reader.GetString(1),
                    RanAt         = DateTime.Parse(reader.GetString(2)),
                    Outcome       = reader.GetString(3),
                    DocumentCount = reader.GetInt32(4),
                    ErrorMessage  = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
            return entries;
        }

        public List<SyncLogEntry> GetLatestPerConnector()
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ConnectorName, RanAt, Outcome, DocumentCount, ErrorMessage
                FROM   SyncLog s1
                WHERE  Id = (
                    SELECT MAX(Id) FROM SyncLog s2 WHERE s2.ConnectorName = s1.ConnectorName
                )
                ORDER  BY ConnectorName
                """;

            var entries = new List<SyncLogEntry>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new SyncLogEntry
                {
                    Id            = reader.GetInt64(0),
                    ConnectorName = reader.GetString(1),
                    RanAt         = DateTime.Parse(reader.GetString(2)),
                    Outcome       = reader.GetString(3),
                    DocumentCount = reader.GetInt32(4),
                    ErrorMessage  = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
            return entries;
        }

        public DateTime? GetLastSuccessfulRun()
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(RanAt) FROM SyncLog WHERE Outcome = 'ok'";
            var raw = cmd.ExecuteScalar() as string;
            return raw is null ? null : DateTime.Parse(raw);
        }
    }
}
