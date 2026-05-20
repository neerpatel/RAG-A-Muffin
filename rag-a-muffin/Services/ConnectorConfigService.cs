using RagAMuffin.Database;
using System.Text.Json;

namespace RagAMuffin.Services
{
    public class ConnectorConfigService
    {
        private readonly AppDatabase _db;
        private readonly ILogger<ConnectorConfigService> _logger;
        private readonly IConfiguration _config;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private volatile ConnectorConfig _current;

        private static readonly JsonSerializerOptions _jsonOpts =
            new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

        public ConnectorConfig Current => _current;

        public ConnectorConfigService(AppDatabase db, ILogger<ConnectorConfigService> logger, IConfiguration config)
        {
            _db      = db;
            _logger  = logger;
            _config  = config;
            _current = LoadOrDefault();
        }

        public async Task<ConnectorConfig> SaveAsync(ConnectorConfig config)
        {
            await _saveLock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(config, _jsonOpts);
                using var conn = _db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO KV (Key, Value, UpdatedAt) VALUES ('connectors', $v, datetime('now'))";
                cmd.Parameters.AddWithValue("$v", json);
                cmd.ExecuteNonQuery();

                _current = config;
                _logger.LogInformation(
                    "Connector config saved: {Feeds} RSS feed(s), {Urls} web URL(s)",
                    config.RssFeeds.Count, config.WebUrls.Count);
                return _current;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private ConnectorConfig LoadOrDefault()
        {
            try
            {
                using var conn = _db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM KV WHERE Key = 'connectors'";
                var raw = cmd.ExecuteScalar() as string;
                if (raw is not null)
                {
                    var loaded = JsonSerializer.Deserialize<ConnectorConfig>(raw, _jsonOpts);
                    if (loaded is not null)
                    {
                        _logger.LogInformation(
                            "Connector config loaded: {Feeds} RSS feed(s), {Urls} web URL(s)",
                            loaded.RssFeeds.Count, loaded.WebUrls.Count);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load connector config — using appsettings defaults");
            }

            return new ConnectorConfig
            {
                RssFeeds = _config.GetSection("Connectors:Rss:Feeds").Get<List<FeedEntry>>() ?? [],
                WebUrls  = _config.GetSection("Connectors:Web:Urls").Get<List<FeedEntry>>() ?? []
            };
        }
    }

    public class ConnectorConfig
    {
        public List<FeedEntry> RssFeeds { get; set; } = [];
        public List<FeedEntry> WebUrls  { get; set; } = [];
        public List<string> EnabledConnectors { get; set; } = ["gmail", "drive", "calendar", "rss", "web", "local"];
        public int SyncIntervalMinutes { get; set; } = 0;
        public List<string> GmailLabels { get; set; } = ["INBOX", "SENT"];
        public List<string> LocalDirectories  { get; set; } = [];
        public List<string> ObsidianVaults    { get; set; } = [];
        public List<string> YoutubeUrls       { get; set; } = [];
        public List<GithubRepoEntry> GithubRepos { get; set; } = [];
        public string? GithubToken            { get; set; }
        public string? BookmarksFilePath      { get; set; }
    }

    public class GithubRepoEntry
    {
        public string Repo        { get; set; } = string.Empty; // "owner/repo"
        public bool   IndexReadme { get; set; } = true;
        public bool   IndexIssues { get; set; } = true;
        public bool   IndexPRs    { get; set; } = false;
    }

    public class FeedEntry
    {
        public string  Url   { get; set; } = string.Empty;
        public string? Label { get; set; }
    }
}
