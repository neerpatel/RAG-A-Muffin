using RagAMuffin.Database;

namespace RagAMuffin.Services
{
    public class Note
    {
        public string   Id        { get; set; } = Guid.NewGuid().ToString("N");
        public string   Title     { get; set; } = string.Empty;
        public string   Body      { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class NoteService
    {
        private readonly AppDatabase _db;
        private readonly ILogger<NoteService> _logger;

        public NoteService(AppDatabase db, ILogger<NoteService> logger)
        {
            _db     = db;
            _logger = logger;
        }

        public List<Note> List()
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Body, CreatedAt, UpdatedAt FROM Notes ORDER BY UpdatedAt DESC";
            var notes = new List<Note>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                notes.Add(ReadNote(reader));
            return notes;
        }

        public Note? Get(string id)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Body, CreatedAt, UpdatedAt FROM Notes WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadNote(reader) : null;
        }

        public Note Save(Note note)
        {
            note.UpdatedAt = DateTime.UtcNow;
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Notes (Id, Title, Body, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $body, $created, $updated)
                ON CONFLICT(Id) DO UPDATE SET Title = $title, Body = $body, UpdatedAt = $updated
                """;
            cmd.Parameters.AddWithValue("$id",      note.Id);
            cmd.Parameters.AddWithValue("$title",   note.Title);
            cmd.Parameters.AddWithValue("$body",    note.Body);
            cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
            _logger.LogInformation("Note saved: {Id}", note.Id);
            return note;
        }

        public void Delete(string id)
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Notes WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        private static Note ReadNote(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
        {
            Id        = r.GetString(0),
            Title     = r.GetString(1),
            Body      = r.GetString(2),
            CreatedAt = DateTime.Parse(r.GetString(3)),
            UpdatedAt = DateTime.Parse(r.GetString(4))
        };
    }
}
