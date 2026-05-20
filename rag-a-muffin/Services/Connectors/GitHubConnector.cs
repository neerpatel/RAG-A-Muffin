using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RagAMuffin.Services.Connectors
{
    public class GitHubConnector : IConnector
    {
        private readonly ConnectorConfigService _connectorConfig;
        private readonly ILogger<GitHubConnector> _logger;

        public string SourceType => "github";

        private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        public GitHubConnector(ConnectorConfigService connectorConfig, ILogger<GitHubConnector> logger)
        {
            _connectorConfig = connectorConfig;
            _logger          = logger;
        }

        public async Task<IEnumerable<SourceDocument>> FetchAsync(CancellationToken ct = default)
        {
            var repos = _connectorConfig.Current.GithubRepos;
            if (repos.Count == 0) return [];

            var token = _connectorConfig.Current.GithubToken;
            using var http = BuildClient(token);

            var documents = new List<SourceDocument>();

            foreach (var entry in repos)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(entry.Repo)) continue;

                _logger.LogInformation("GitHubConnector: processing '{Repo}'", entry.Repo);

                if (entry.IndexReadme)
                {
                    var readme = await FetchReadmeAsync(http, entry.Repo, ct);
                    if (readme is not null) documents.Add(readme);
                }

                if (entry.IndexIssues)
                    documents.AddRange(await FetchIssuesAsync(http, entry.Repo, isPR: false, ct));

                if (entry.IndexPRs)
                    documents.AddRange(await FetchIssuesAsync(http, entry.Repo, isPR: true, ct));
            }

            _logger.LogInformation("GitHubConnector: indexed {Count} document(s)", documents.Count);
            return documents;
        }

        private async Task<SourceDocument?> FetchReadmeAsync(HttpClient http, string repo, CancellationToken ct)
        {
            try
            {
                var res = await http.GetAsync($"https://api.github.com/repos/{repo}/readme", ct);
                if (!res.IsSuccessStatusCode) return null;

                var json    = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var encoded = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
                var content = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Replace("\n", "")));

                var docId = StableId($"github-readme:{repo}");
                return new SourceDocument
                {
                    Id          = docId,
                    SourceType  = SourceType,
                    Title       = $"{repo} README",
                    Author      = repo,
                    Url         = $"https://github.com/{repo}",
                    Body        = content,
                    PublishedAt = DateTime.UtcNow,
                    Metadata    = new Dictionary<string, string> { ["repo"] = repo, ["kind"] = "readme" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GitHubConnector: failed to fetch README for '{Repo}'", repo);
                return null;
            }
        }

        private async Task<List<SourceDocument>> FetchIssuesAsync(HttpClient http, string repo, bool isPR, CancellationToken ct)
        {
            var kind     = isPR ? "pulls" : "issues";
            var kindLabel = isPR ? "PR" : "issue";
            var documents = new List<SourceDocument>();

            try
            {
                var page = 1;
                while (true)
                {
                    var url = $"https://api.github.com/repos/{repo}/{kind}?state=all&per_page=100&page={page}";
                    var res = await http.GetAsync(url, ct);
                    if (!res.IsSuccessStatusCode) break;

                    var json  = await res.Content.ReadAsStringAsync(ct);
                    var items = JsonSerializer.Deserialize<List<GitHubIssue>>(json, _json) ?? [];
                    if (items.Count == 0) break;

                    foreach (var item in items)
                    {
                        if (isPR && item.PullRequest is null) continue;
                        if (!isPR && item.PullRequest is not null) continue;

                        var body  = $"#{item.Number}: {item.Title}\n\n{item.Body ?? string.Empty}".Trim();
                        var docId = StableId($"github-{kindLabel}:{repo}#{item.Number}");

                        documents.Add(new SourceDocument
                        {
                            Id          = docId,
                            SourceType  = SourceType,
                            Title       = $"[{repo}] #{item.Number}: {item.Title}",
                            Author      = item.User?.Login ?? repo,
                            Url         = item.HtmlUrl ?? $"https://github.com/{repo}/{kind}/{item.Number}",
                            Body        = body,
                            PublishedAt = item.CreatedAt ?? DateTime.UtcNow,
                            Metadata    = new Dictionary<string, string>
                            {
                                ["repo"]   = repo,
                                ["kind"]   = kindLabel,
                                ["number"] = item.Number.ToString(),
                                ["state"]  = item.State ?? "unknown"
                            }
                        });
                    }

                    if (items.Count < 100) break;
                    page++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GitHubConnector: failed to fetch {Kind} for '{Repo}'", kindLabel, repo);
            }

            return documents;
        }

        private static HttpClient BuildClient(string? token)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RagAMuffin/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        private static string StableId(string key) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        private class GitHubIssue
        {
            public int Number { get; set; }
            public string? Title { get; set; }
            public string? Body { get; set; }
            public string? State { get; set; }
            public string? HtmlUrl { get; set; }
            public DateTime? CreatedAt { get; set; }
            public GitHubUser? User { get; set; }
            public object? PullRequest { get; set; }
        }

        private class GitHubUser { public string? Login { get; set; } }
    }
}
