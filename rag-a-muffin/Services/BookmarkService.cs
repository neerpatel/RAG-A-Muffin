using RagAMuffin.Database;

namespace RagAMuffin.Services
{
    public class Bookmark
    {
        public string   Id        { get; set; } = Guid.NewGuid().ToString("N");
        public string   Title     { get; set; } = string.Empty;
        public string   Content   { get; set; } = string.Empty;
        public string?  SessionId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BookmarkService
    {
        private readonly AppDatabase _db;

        public BookmarkService(AppDatabase db) => _db = db;

        public List<Bookmark> List()
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Content, SessionId, CreatedAt FROM Bookmarks ORDER BY CreatedAt DESC";
            var bookmarks = new List<Bookmark>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bookmarks.Add(ReadBookmark(reader));
            return bookmarks;
        }

        public Bookmark Save(Bookmark bookmark)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Bookmarks (Id, Title, Content, SessionId, CreatedAt)
                VALUES ($id, $title, $content, $session, $created)
                ON CONFLICT(Id) DO UPDATE SET Title = $title, Content = $content
                """;
            cmd.Parameters.AddWithValue("$id",      bookmark.Id);
            cmd.Parameters.AddWithValue("$title",   bookmark.Title);
            cmd.Parameters.AddWithValue("$content", bookmark.Content);
            cmd.Parameters.AddWithValue("$session", (object?)bookmark.SessionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", bookmark.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
            return bookmark;
        }

        public void Delete(string id)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Bookmarks WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        private static Bookmark ReadBookmark(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
        {
            Id        = r.GetString(0),
            Title     = r.GetString(1),
            Content   = r.GetString(2),
            SessionId = r.IsDBNull(3) ? null : r.GetString(3),
            CreatedAt = DateTime.Parse(r.GetString(4))
        };
    }
}
