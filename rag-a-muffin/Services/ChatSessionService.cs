using Microsoft.Data.Sqlite;
using RagAMuffin.Database;
using RagAMuffin.Models;

namespace RagAMuffin.Services
{
    public class ChatSessionService
    {
        private readonly AppDatabase _db;
        private readonly ILogger<ChatSessionService> _logger;

        public ChatSessionService(AppDatabase db, ILogger<ChatSessionService> logger)
        {
            _db     = db;
            _logger = logger;
        }

        public Task<List<ChatSession>> ListAsync()
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Title, CreatedAt, UpdatedAt
                FROM   ChatSessions
                ORDER  BY UpdatedAt DESC
                """;

            var sessions = new List<ChatSession>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new ChatSession
                {
                    Id        = reader.GetString(0),
                    Title     = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    UpdatedAt = DateTime.Parse(reader.GetString(3)),
                });
            }

            // Load messages for each session
            foreach (var session in sessions)
                LoadMessages(conn, session);

            return Task.FromResult(sessions);
        }

        public Task<ChatSession?> GetAsync(string id)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, CreatedAt, UpdatedAt FROM ChatSessions WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return Task.FromResult<ChatSession?>(null);

            var session = new ChatSession
            {
                Id        = reader.GetString(0),
                Title     = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                UpdatedAt = DateTime.Parse(reader.GetString(3)),
            };
            reader.Close();

            LoadMessages(conn, session);
            return Task.FromResult<ChatSession?>(session);
        }

        public Task<ChatSession> SaveAsync(ChatSession session)
        {
            using var conn = _db.Open();
            using var tx   = conn.BeginTransaction();

            using var upsertSession = conn.CreateCommand();
            upsertSession.Transaction = tx;
            upsertSession.CommandText = """
                INSERT INTO ChatSessions (Id, Title, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $created, $updated)
                ON CONFLICT(Id) DO UPDATE SET Title = $title, UpdatedAt = $updated
                """;
            upsertSession.Parameters.AddWithValue("$id",      session.Id);
            upsertSession.Parameters.AddWithValue("$title",   session.Title);
            upsertSession.Parameters.AddWithValue("$created", session.CreatedAt.ToString("O"));
            upsertSession.Parameters.AddWithValue("$updated", session.UpdatedAt.ToString("O"));
            upsertSession.ExecuteNonQuery();

            using var delMsgs = conn.CreateCommand();
            delMsgs.Transaction = tx;
            delMsgs.CommandText = "DELETE FROM ChatMessages WHERE SessionId = $sid";
            delMsgs.Parameters.AddWithValue("$sid", session.Id);
            delMsgs.ExecuteNonQuery();

            for (int i = 0; i < session.Messages.Count; i++)
            {
                var m = session.Messages[i];
                using var insMsg = conn.CreateCommand();
                insMsg.Transaction = tx;
                insMsg.CommandText = """
                    INSERT INTO ChatMessages (SessionId, Ordinal, Role, Content, CitationsJson)
                    VALUES ($sid, $ord, $role, $content, $cit)
                    """;
                insMsg.Parameters.AddWithValue("$sid",     session.Id);
                insMsg.Parameters.AddWithValue("$ord",     i);
                insMsg.Parameters.AddWithValue("$role",    m.Role);
                insMsg.Parameters.AddWithValue("$content", m.Content);
                insMsg.Parameters.AddWithValue("$cit",     (object?)m.CitationsJson ?? DBNull.Value);
                insMsg.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.FromResult(session);
        }

        public Task DeleteAsync(string id)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ChatSessions WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }

        public Task<List<ChatSession>> SearchAsync(string q)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT s.Id, s.Title, s.CreatedAt, s.UpdatedAt
                FROM   ChatSessions s
                LEFT   JOIN ChatMessages m ON m.SessionId = s.Id
                WHERE  s.Title LIKE $q OR m.Content LIKE $q
                ORDER  BY s.UpdatedAt DESC
                LIMIT  50
                """;
            cmd.Parameters.AddWithValue("$q", $"%{q}%");

            var sessions = new List<ChatSession>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new ChatSession
                {
                    Id        = reader.GetString(0),
                    Title     = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    UpdatedAt = DateTime.Parse(reader.GetString(3)),
                });
            }
            return Task.FromResult(sessions);
        }

        private static void LoadMessages(SqliteConnection conn, ChatSession session)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Role, Content, CitationsJson
                FROM   ChatMessages
                WHERE  SessionId = $sid
                ORDER  BY Ordinal
                """;
            cmd.Parameters.AddWithValue("$sid", session.Id);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                session.Messages.Add(new ChatMessage
                {
                    Role          = reader.GetString(0),
                    Content       = reader.GetString(1),
                    CitationsJson = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }
        }
    }
}
