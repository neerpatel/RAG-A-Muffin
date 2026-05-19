using RagAMuffin.Database;
using RagAMuffin.Models;
using System.Text.Json;

namespace RagAMuffin.Services
{
    public class SettingsService
    {
        private readonly AppDatabase _db;
        private readonly IConfiguration _config;
        private static readonly JsonSerializerOptions _jsonOpts =
            new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

        private volatile AppSettings _current;
        public AppSettings Current => _current;

        public SettingsService(AppDatabase db, IConfiguration config)
        {
            _db      = db;
            _config  = config;
            _current = LoadOrDefault();
        }

        public async Task<AppSettings> SaveAsync(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, _jsonOpts);
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO KV (Key, Value, UpdatedAt) VALUES ('settings', $v, datetime('now'))";
            cmd.Parameters.AddWithValue("$v", json);
            cmd.ExecuteNonQuery();
            _current = settings;
            return await Task.FromResult(_current);
        }

        private AppSettings LoadOrDefault()
        {
            try
            {
                using var conn = _db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM KV WHERE Key = 'settings'";
                var raw = cmd.ExecuteScalar() as string;
                if (raw is not null)
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(raw, _jsonOpts);
                    if (loaded is not null) return loaded;
                }
            }
            catch { /* fall through to defaults */ }

            return new AppSettings
            {
                LlmModel = _config.GetValue("Ollama:LlmModel", "llama3.2")!
            };
        }
    }
}
