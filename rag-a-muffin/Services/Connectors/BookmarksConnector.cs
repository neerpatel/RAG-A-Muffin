using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RagAMuffin.Services.Connectors
{
    // Indexes a Chrome/Firefox HTML bookmark export (Netscape Bookmark Format).
    // Place the exported file at the path configured in BookmarksFilePath.
    // Each bookmark is scraped via the existing HttpClient; duplicates are skipped.
    public class BookmarksConnector : IConnector
    {
        private readonly ConnectorConfigService _connectorConfig;
        private readonly ILogger<BookmarksConnector> _logger;

        public string SourceType => "bookmark";

        public BookmarksConnector(ConnectorConfigService connectorConfig, ILogger<BookmarksConnector> logger)
        {
            _connectorConfig = connectorConfig;
            _logger          = logger;
        }

        public async Task<IEnumerable<SourceDocument>> FetchAsync(CancellationToken ct = default)
        {
            var path = _connectorConfig.Current.BookmarksFilePath;
            if (string.IsNullOrWhiteSpace(path)) return [];

            if (!File.Exists(path))
            {
                _logger.LogWarning("BookmarksConnector: file '{Path}' not found", path);
                return [];
            }

            string html;
            try { html = await File.ReadAllTextAsync(path, Encoding.UTF8, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookmarksConnector: failed to read '{Path}'", path);
                return [];
            }

            var bookmarks = ParseNetscapeHtml(html);
            _logger.LogInformation("BookmarksConnector: found {Count} bookmark(s)", bookmarks.Count);

            var documents = new List<SourceDocument>();
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RagAMuffin/1.0)");

            foreach (var (url, title, addDate) in bookmarks)
            {
                if (ct.IsCancellationRequested) break;

                var docId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"bookmark:{url}")))
                    .ToLowerInvariant();

                string body;
                try
                {
                    var pageHtml = await http.GetStringAsync(url, ct);
                    body = ExtractText(pageHtml);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "BookmarksConnector: failed to scrape '{Url}' — indexing title only", url);
                    body = title;
                }

                if (string.IsNullOrWhiteSpace(body)) body = title;

                var published = addDate > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(addDate).UtcDateTime
                    : DateTime.UtcNow;

                documents.Add(new SourceDocument
                {
                    Id          = docId,
                    SourceType  = SourceType,
                    Title       = title,
                    Author      = new Uri(url).Host,
                    Url         = url,
                    Body        = body,
                    PublishedAt = published,
                    Metadata    = new Dictionary<string, string> { ["bookmarkUrl"] = url }
                });

                _logger.LogInformation("BookmarksConnector: indexed '{Title}'", title);
            }

            _logger.LogInformation("BookmarksConnector: indexed {Count} bookmark(s)", documents.Count);
            return documents;
        }

        private static List<(string url, string title, long addDate)> ParseNetscapeHtml(string html)
        {
            var results  = new List<(string, string, long)>();
            // Match <A HREF="url" ADD_DATE="epoch" ...>title</A>
            var pattern  = new Regex(@"<A\s[^>]*HREF=""([^""]+)""[^>]*(?:ADD_DATE=""(\d+)"")?[^>]*>([^<]+)</A>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in pattern.Matches(html))
            {
                var url     = m.Groups[1].Value.Trim();
                var addDate = long.TryParse(m.Groups[2].Value, out var ts) ? ts : 0;
                var title   = System.Net.WebUtility.HtmlDecode(m.Groups[3].Value.Trim());

                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                results.Add((url, title, addDate));
            }

            return results;
        }

        private static string ExtractText(string html)
        {
            // Strip scripts and styles
            html = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Strip remaining tags
            html = Regex.Replace(html, @"<[^>]+>", " ");
            // Collapse whitespace
            html = Regex.Replace(html, @"\s{2,}", " ");
            return System.Net.WebUtility.HtmlDecode(html).Trim();
        }
    }
}
