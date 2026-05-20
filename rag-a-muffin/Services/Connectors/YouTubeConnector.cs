using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RagAMuffin.Services.Connectors
{
    public class YouTubeConnector : IConnector
    {
        private readonly ConnectorConfigService _connectorConfig;
        private readonly HttpClient _http;
        private readonly ILogger<YouTubeConnector> _logger;

        public string SourceType => "youtube";

        public YouTubeConnector(ConnectorConfigService connectorConfig, ILogger<YouTubeConnector> logger)
        {
            _connectorConfig = connectorConfig;
            _logger          = logger;
            _http            = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RagAMuffin/1.0)");
        }

        public async Task<IEnumerable<SourceDocument>> FetchAsync(CancellationToken ct = default)
        {
            var urls = _connectorConfig.Current.YoutubeUrls;
            if (urls.Count == 0) return [];

            var documents = new List<SourceDocument>();

            foreach (var url in urls)
            {
                if (ct.IsCancellationRequested) break;

                var videoId = ExtractVideoId(url);
                if (videoId is null)
                {
                    _logger.LogWarning("YouTubeConnector: cannot parse video ID from '{Url}'", url);
                    continue;
                }

                try
                {
                    var doc = await FetchVideoAsync(videoId, url, ct);
                    if (doc is not null) documents.Add(doc);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "YouTubeConnector: failed to fetch '{VideoId}'", videoId);
                }
            }

            _logger.LogInformation("YouTubeConnector: indexed {Count} video(s)", documents.Count);
            return documents;
        }

        private async Task<SourceDocument?> FetchVideoAsync(string videoId, string url, CancellationToken ct)
        {
            // Fetch the video page to extract title and transcript URL
            var pageHtml = await _http.GetStringAsync($"https://www.youtube.com/watch?v={videoId}", ct);

            var title    = ExtractTitle(pageHtml) ?? videoId;
            var channel  = ExtractChannel(pageHtml) ?? "YouTube";
            var dateStr  = ExtractUploadDate(pageHtml);
            var date     = DateTime.TryParse(dateStr, out var d) ? d : DateTime.UtcNow;

            var transcriptBody = await FetchTranscriptAsync(pageHtml, ct);
            if (string.IsNullOrWhiteSpace(transcriptBody))
            {
                _logger.LogWarning("YouTubeConnector: no transcript available for '{VideoId}'", videoId);
                return null;
            }

            var docId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"youtube:{videoId}")))
                .ToLowerInvariant();

            _logger.LogInformation("YouTubeConnector: indexed '{Title}' ({VideoId})", title, videoId);

            return new SourceDocument
            {
                Id          = docId,
                SourceType  = SourceType,
                Title       = title,
                Author      = channel,
                Url         = url,
                Body        = transcriptBody,
                PublishedAt = date,
                Metadata    = new Dictionary<string, string> { ["videoId"] = videoId }
            };
        }

        private async Task<string?> FetchTranscriptAsync(string pageHtml, CancellationToken ct)
        {
            // Extract the timedtext base URL from the player response JSON
            var match = Regex.Match(pageHtml, @"""baseUrl""\s*:\s*""(https://www\.youtube\.com/api/timedtext[^""]+)""");
            if (!match.Success) return null;

            var transcriptUrl = Regex.Unescape(match.Groups[1].Value);

            string xml;
            try { xml = await _http.GetStringAsync(transcriptUrl, ct); }
            catch { return null; }

            // Parse transcript XML: <text start="..." dur="...">caption text</text>
            try
            {
                var doc   = XDocument.Parse(xml);
                var lines = doc.Descendants("text")
                    .Select(e => System.Net.WebUtility.HtmlDecode(e.Value.Trim()))
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                return string.Join(' ', lines);
            }
            catch { return null; }
        }

        private static string? ExtractVideoId(string url)
        {
            var m = Regex.Match(url, @"(?:v=|youtu\.be/)([A-Za-z0-9_-]{11})");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string? ExtractTitle(string html)
        {
            var m = Regex.Match(html, @"""title""\s*:\s*\{""runs""\s*:\s*\[\{""text""\s*:\s*""([^""]+)""");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(html, @"<title>([^<]+)</title>");
            return m.Success ? m.Groups[1].Value.Replace(" - YouTube", "").Trim() : null;
        }

        private static string? ExtractChannel(string html)
        {
            var m = Regex.Match(html, @"""ownerChannelName""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string? ExtractUploadDate(string html)
        {
            var m = Regex.Match(html, @"""uploadDate""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
