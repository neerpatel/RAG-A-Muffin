using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace RagAMuffin.Database
{
    /// <summary>
    /// Owns the SQLite connection string, creates the schema on first run,
    /// and migrates existing JSON files into the database once.
    /// </summary>
    public class AppDatabase
    {
        public const string DbPath = "/app/data/app.db";
        public string ConnectionString { get; } = $"Data Source={DbPath};Cache=Shared";

        private readonly ILogger<AppDatabase> _logger;
        private readonly IConfiguration _config;

        public AppDatabase(ILogger<AppDatabase> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            Directory.CreateDirectory("/app/data");
            InitializeSchema();
            RunJsonMigrations();
        }

        // Test constructor — accepts a connection string directly, skips file migration
        internal AppDatabase(ILogger<AppDatabase> logger, string connectionString)
        {
            _logger = logger;
            _config = null!;
            ConnectionString = connectionString;
            InitializeSchema();
        }

        public SqliteConnection Open()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        private void InitializeSchema()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;

                CREATE TABLE IF NOT EXISTS KV (
                    Key       TEXT PRIMARY KEY,
                    Value     TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                );

                CREATE TABLE IF NOT EXISTS ChatSessions (
                    Id        TEXT PRIMARY KEY,
                    Title     TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ChatMessages (
                    RowId        INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId    TEXT    NOT NULL REFERENCES ChatSessions(Id) ON DELETE CASCADE,
                    Ordinal      INTEGER NOT NULL,
                    Role         TEXT    NOT NULL,
                    Content      TEXT    NOT NULL,
                    CitationsJson TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_messages_session
                    ON ChatMessages(SessionId, Ordinal);

                CREATE TABLE IF NOT EXISTS SyncLog (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConnectorName TEXT    NOT NULL,
                    RanAt         TEXT    NOT NULL,
                    Outcome       TEXT    NOT NULL,
                    DocumentCount INTEGER NOT NULL DEFAULT 0,
                    ErrorMessage  TEXT
                );

                CREATE TABLE IF NOT EXISTS Notes (
                    Id        TEXT PRIMARY KEY,
                    Title     TEXT NOT NULL,
                    Body      TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Bookmarks (
                    Id        TEXT PRIMARY KEY,
                    Title     TEXT NOT NULL,
                    Content   TEXT NOT NULL,
                    SessionId TEXT,
                    CreatedAt TEXT NOT NULL
                );

                CREATE VIRTUAL TABLE IF NOT EXISTS ChunksFTS USING fts5(
                    text,
                    documentId  UNINDEXED,
                    chunkIndex  UNINDEXED,
                    sourceType  UNINDEXED,
                    publishedAt UNINDEXED,
                    title       UNINDEXED,
                    author      UNINDEXED,
                    parentText  UNINDEXED,
                    tokenize = 'porter unicode61'
                );
                """;
            cmd.ExecuteNonQuery();
        }

        private void RunJsonMigrations()
        {
            using var conn = Open();

            // Check migration flag
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT Value FROM KV WHERE Key = 'migration.json_imported'";
            if (checkCmd.ExecuteScalar() is not null) return;

            _logger.LogInformation("Running one-time JSON → SQLite migration...");

            MigrateConnectorConfig(conn);
            MigrateSettings(conn);
            MigrateChatSessions(conn);

            // Mark done
            using var flagCmd = conn.CreateCommand();
            flagCmd.CommandText = "INSERT OR REPLACE INTO KV (Key, Value, UpdatedAt) VALUES ('migration.json_imported', 'true', datetime('now'))";
            flagCmd.ExecuteNonQuery();

            _logger.LogInformation("JSON → SQLite migration complete");
        }

        private void MigrateConnectorConfig(SqliteConnection conn)
        {
            const string path = "/app/data/connectors.json";
            if (!File.Exists(path)) return;
            try
            {
                var json = File.ReadAllText(path);
                Upsert(conn, "connectors", json);
                _logger.LogInformation("Migrated connectors.json");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to migrate connectors.json"); }
        }

        private void MigrateSettings(SqliteConnection conn)
        {
            const string path = "/app/data/settings.json";
            if (!File.Exists(path)) return;
            try
            {
                var json = File.ReadAllText(path);
                Upsert(conn, "settings", json);
                _logger.LogInformation("Migrated settings.json");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to migrate settings.json"); }
        }

        private void MigrateChatSessions(SqliteConnection conn)
        {
            const string dir = "/app/data/chats";
            if (!Directory.Exists(dir)) return;

            var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            int count = 0;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<LegacySession>(json, opts);
                    if (session is null || string.IsNullOrWhiteSpace(session.Id)) continue;

                    // Skip if already in DB
                    using var existCmd = conn.CreateCommand();
                    existCmd.CommandText = "SELECT 1 FROM ChatSessions WHERE Id = $id";
                    existCmd.Parameters.AddWithValue("$id", session.Id);
                    if (existCmd.ExecuteScalar() is not null) continue;

                    using var tx = conn.BeginTransaction();
                    using var insSession = conn.CreateCommand();
                    insSession.Transaction = tx;
                    insSession.CommandText = """
                        INSERT INTO ChatSessions (Id, Title, CreatedAt, UpdatedAt)
                        VALUES ($id, $title, $created, $updated)
                        """;
                    insSession.Parameters.AddWithValue("$id",      session.Id);
                    insSession.Parameters.AddWithValue("$title",   session.Title ?? "Chat");
                    insSession.Parameters.AddWithValue("$created", session.CreatedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"));
                    insSession.Parameters.AddWithValue("$updated", session.UpdatedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"));
                    insSession.ExecuteNonQuery();

                    var msgs = session.Messages ?? [];
                    for (int i = 0; i < msgs.Count; i++)
                    {
                        var m = msgs[i];
                        using var insMsg = conn.CreateCommand();
                        insMsg.Transaction = tx;
                        insMsg.CommandText = """
                            INSERT INTO ChatMessages (SessionId, Ordinal, Role, Content, CitationsJson)
                            VALUES ($sid, $ord, $role, $content, $cit)
                            """;
                        insMsg.Parameters.AddWithValue("$sid",     session.Id);
                        insMsg.Parameters.AddWithValue("$ord",     i);
                        insMsg.Parameters.AddWithValue("$role",    m.Role ?? "user");
                        insMsg.Parameters.AddWithValue("$content", m.Content ?? "");
                        insMsg.Parameters.AddWithValue("$cit",     (object?)m.CitationsJson ?? DBNull.Value);
                        insMsg.ExecuteNonQuery();
                    }
                    tx.Commit();
                    count++;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to migrate session {File}", file); }
            }

            if (count > 0) _logger.LogInformation("Migrated {Count} chat session(s)", count);
        }

        private static void Upsert(SqliteConnection conn, string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO KV (Key, Value, UpdatedAt) VALUES ($k, $v, datetime('now'))";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        // Minimal types used only for JSON migration
        private sealed class LegacySession
        {
            public string?             Id        { get; set; }
            public string?             Title     { get; set; }
            public DateTime?           CreatedAt { get; set; }
            public DateTime?           UpdatedAt { get; set; }
            public List<LegacyMessage> Messages  { get; set; } = [];
        }

        private sealed class LegacyMessage
        {
            public string? Role         { get; set; }
            public string? Content      { get; set; }
            public string? CitationsJson { get; set; }
        }
    }
}
