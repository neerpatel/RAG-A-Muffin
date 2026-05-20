using RagAMuffin.Models;
using RagAMuffin.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RagAMuffin.Services.Connectors
{
    public class ObsidianConnector : IConnector
    {
        private readonly ConnectorConfigService _connectorConfig;
        private readonly ILogger<ObsidianConnector> _logger;
        private static readonly long MaxBytes = 10 * 1024 * 1024;

        public string SourceType => "obsidian";

        public ObsidianConnector(ConnectorConfigService connectorConfig, ILogger<ObsidianConnector> logger)
        {
            _connectorConfig = connectorConfig;
            _logger          = logger;
        }

        public async Task<IEnumerable<SourceDocument>> FetchAsync(CancellationToken ct = default)
        {
            var vaults = _connectorConfig.Current.ObsidianVaults;
            if (vaults.Count == 0) return [];

            var documents = new List<SourceDocument>();

            foreach (var vault in vaults)
            {
                if (!Directory.Exists(vault))
                {
                    _logger.LogWarning("ObsidianConnector: vault '{Vault}' does not exist — skipping", vault);
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(vault, "*.md", SearchOption.AllDirectories)
                        .Where(f => !Path.GetFileName(f).StartsWith('.'));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ObsidianConnector: failed to enumerate '{Vault}'", vault);
                    continue;
                }

                foreach (var filePath in files)
                {
                    if (ct.IsCancellationRequested) break;

                    var fi = new FileInfo(filePath);
                    if (fi.Length > MaxBytes)
                    {
                        _logger.LogWarning("ObsidianConnector: '{File}' exceeds 10 MB — skipping", filePath);
                        continue;
                    }

                    string raw;
                    try { raw = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "ObsidianConnector: failed to read '{File}'", filePath);
                        continue;
                    }

                    var (title, date, tags, body) = ParseNote(raw, filePath);
                    if (string.IsNullOrWhiteSpace(body)) continue;

                    var docId = Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes($"obsidian:{Path.GetFullPath(filePath)}")))
                        .ToLowerInvariant();

                    var metadata = new Dictionary<string, string>
                    {
                        ["filePath"] = filePath,
                        ["filename"] = Path.GetFileName(filePath),
                        ["vault"]    = vault,
                    };
                    if (tags.Count > 0) metadata["tags"] = string.Join(", ", tags);

                    documents.Add(new SourceDocument
                    {
                        Id          = docId,
                        SourceType  = SourceType,
                        Title       = title,
                        Author      = "obsidian",
                        Body        = body,
                        PublishedAt = date ?? fi.LastWriteTimeUtc,
                        Metadata    = metadata
                    });
                }
            }

            _logger.LogInformation("ObsidianConnector: indexed {Count} note(s)", documents.Count);
            return documents;
        }

        private static (string title, DateTime? date, List<string> tags, string body) ParseNote(string raw, string filePath)
        {
            var title = Path.GetFileNameWithoutExtension(filePath);
            DateTime? date = null;
            var tags = new List<string>();
            var body = raw;

            // Strip YAML frontmatter
            if (raw.StartsWith("---"))
            {
                var end = raw.IndexOf("\n---", 3);
                if (end > 0)
                {
                    var frontmatter = raw[3..end];
                    body = raw[(end + 4)..].TrimStart();

                    foreach (var line in frontmatter.Split('\n'))
                    {
                        var colon = line.IndexOf(':');
                        if (colon < 0) continue;
                        var key = line[..colon].Trim().ToLowerInvariant();
                        var val = line[(colon + 1)..].Trim().Trim('"');

                        if (key is "title" && !string.IsNullOrWhiteSpace(val))
                            title = val;
                        else if (key is "date" or "created" && DateTime.TryParse(val, out var d))
                            date = d;
                        else if (key is "tags")
                            tags.AddRange(val.Trim('[', ']').Split(',').Select(t => t.Trim()));
                    }
                }
            }

            // Resolve wikilinks [[Note Title]] → Note Title
            body = Regex.Replace(body, @"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]", "$1");

            // Remove inline tags (#tag) and keep the word
            body = Regex.Replace(body, @"(?<!\w)#([A-Za-z][A-Za-z0-9_/-]*)", "$1");

            // Strip markdown image syntax
            body = Regex.Replace(body, @"!\[.*?\]\(.*?\)", string.Empty);

            return (title, date, tags, body.Trim());
        }
    }
}
