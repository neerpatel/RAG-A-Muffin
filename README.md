<div align="center">
  <img src="assets/logo.svg" alt="RAG-A-Muffin" width="720" />
  <br/><br/>

  [![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
  [![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat-square&logo=docker&logoColor=white)](https://www.docker.com)
  [![Ollama](https://img.shields.io/badge/Ollama-Local%20LLM-000000?style=flat-square)](https://ollama.com)
  [![Qdrant](https://img.shields.io/badge/Qdrant-Vector%20DB-DC244C?style=flat-square)](https://qdrant.tech)
  [![License: MIT](https://img.shields.io/badge/License-MIT-4ade80?style=flat-square)](#)
</div>

---

A local, privacy-first RAG (Retrieval-Augmented Generation) system that runs on a Raspberry Pi or any homelab device. Connect your Gmail, Google Drive, Google Calendar, RSS feeds, websites, local directories, Obsidian vault, YouTube videos, GitHub repos, and browser bookmarks — then ask questions about your life in a streaming chat UI with inline citations, all staying on your hardware.

## Why RAG-A-Muffin?

- **100% private** — your data never leaves your device. No cloud uploads, no third-party APIs processing your personal information.
- **Local AI** — uses [Ollama](https://ollama.com) for on-device LLM inference and embeddings. Models run entirely locally.
- **Multi-source** — Gmail, Drive, Calendar, RSS feeds, web scraping, local directories, Obsidian vaults, YouTube transcripts, GitHub repos, browser bookmarks, file uploads, and a watch folder all feed into one searchable index.
- **Smarter retrieval** — hybrid BM25 + vector search with Reciprocal Rank Fusion, parent-document context windows, optional query rewriting, and inline `[1]` `[2]` citations in every answer.
- **Homelab-friendly** — designed to run on Raspberry Pi and other resource-constrained devices. GPU acceleration available for NVIDIA and AMD cards.

---

## Quick Start

### Prerequisites

- [Docker](https://www.docker.com/products/docker-desktop) and Docker Compose
- A `credentials.json` file from Google Cloud (see [Google Setup](#google-setup) below)

### 1. Clone and place credentials

```bash
git clone https://github.com/youruser/rag-a-muffin.git
cd rag-a-muffin
mkdir -p data
# Place your credentials.json in ./data/ (see Google Setup)
```

### 2. Start everything

```bash
docker compose up -d
```

This starts three services:

| Service | Port | Purpose |
|---|---|---|
| RAG-A-Muffin | `8000` | Web UI + API |
| Qdrant | `6333 / 6334` | Vector database |
| Ollama | `11434` | Local LLM + embeddings |

Ollama pulls `nomic-embed-text` and `llama3` automatically on first boot. This takes a few minutes.

### 3. Open the UI

Navigate to **http://localhost:8000**.

On first boot you'll see a setup screen — enter your Gmail address and click **Connect Gmail** to authorize. Your credentials are stored locally in `./data/tokens/` and never leave your machine.

### 4. Access from any device on your network

Because RAG-A-Muffin runs as a Docker service bound to port `8000`, any device on the same local network can reach it — phone, tablet, second laptop — without any extra configuration.

Find your host machine's local IP:

```bash
# Linux / macOS
ip route get 1 | awk '{print $7; exit}'

# Windows
ipconfig | findstr "IPv4"
```

Then open **http://\<host-ip\>:8000** on any device connected to the same Wi-Fi or LAN.

> If your host machine has a firewall, make sure port `8000` is allowed for inbound connections from your local network.

---

## Google Setup

RAG-A-Muffin uses your own Google Cloud credentials to access Gmail, Drive, and Calendar. Your data is fetched directly from Google to your device — no intermediary server.

### Step 1 — Create a Google Cloud Project

1. Go to [console.cloud.google.com](https://console.cloud.google.com) and sign in.
2. Click **Select a project → New Project**, give it any name, click **Create**.

### Step 2 — Enable APIs

In **APIs & Services → Library**, enable all three:

- **Gmail API**
- **Google Drive API**
- **Google Calendar API**

### Step 3 — Configure the OAuth consent screen

1. Go to **APIs & Services → OAuth consent screen**.
2. Choose **External**, click **Create**.
3. Fill in an app name (e.g. "RAG-A-Muffin") and your email. Click through the remaining screens.
4. On the **Scopes** screen you can skip adding scopes — the app requests them at sign-in time.
5. On the **Test users** screen, add your Gmail address.

### Step 4 — Create OAuth credentials

1. Go to **APIs & Services → Credentials → Create Credentials → OAuth client ID**.
2. Select **Desktop app** as the application type.
3. Click **Create**, then **Download JSON**.
4. Rename the file to `credentials.json` and place it in the `./data/` folder (next to `app.db`). Create the folder if it doesn't exist yet.

### Step 5 — Authorize in the UI

Start the app and open **http://localhost:8000**. Enter your Gmail address and click **Connect Gmail**. A Google sign-in tab opens — complete sign-in there, then click **I've signed in** in the app.

The app requests read-only access to Gmail, Drive, and Calendar in one authorization. If you previously authorized with Gmail only, you'll need to re-authorize to unlock Drive and Calendar sync.

> Google may show an "unverified app" warning since these are your own developer credentials. Click **Continue** to proceed.

---

## Data Sources

All sources funnel into the same vector store. Use the **source filter chips** (behind the **Filters ▾** pill in the chat UI) to scope queries to a specific source type.

### Gmail

Automatically syncs email on a configurable schedule (default: every 60 minutes). Email threads are deduplicated, quoted reply text is stripped, and signatures are removed before indexing.

**Label filter** — configure which Gmail label IDs to sync from the **Sources** panel (e.g. `INBOX`, `SENT`, `STARRED`). Defaults to `INBOX` and `SENT`. Messages that appear in multiple labels are fetched once.

**Attachment indexing** — PDF and DOCX attachments are automatically downloaded and indexed alongside the email. The attachment is treated as a separate document linked to the parent message.

### Google Drive

Syncs files from your Drive on each sync cycle. Supported formats:

- **Google Docs** — exported as plain text
- **Google Sheets** — exported as CSV
- **PDF, DOCX, TXT, MD** — downloaded and extracted locally

Configure which folders to sync in `appsettings.json` under `Connectors:Drive:FolderIds`. Leave the array empty to sync recent files from your entire Drive.

### Google Calendar

Syncs events from your primary calendar. Configure the date window in `appsettings.json`:

```json
"Calendar": {
  "DaysBack": 30,
  "DaysAhead": 7
}
```

Event title, time, location, attendees, and description are all indexed.

### RSS / Atom Feeds

Add feed URLs through the **Sources** panel or in `appsettings.json`:

```json
"Rss": {
  "Feeds": [
    { "Url": "https://simonwillison.net/atom/everything/", "Label": "Simon Willison" },
    { "Url": "https://hnrss.org/frontpage", "Label": "Hacker News" }
  ]
}
```

Each article is deduplicated by its feed item ID — already-indexed articles are skipped on subsequent syncs.

### Web URL Scraper

Scrape static pages and index their text content. Configure URLs through the **Sources** panel or in `appsettings.json`:

```json
"Web": {
  "Urls": [
    { "Url": "https://wiki.example.com/my-page", "Label": "My Wiki" }
  ]
}
```

Pages are indexed once per URL. To re-index a changed page, remove the entry and re-add it.

### Local Directory

Index any directory bind-mounted into the container. Configure paths through the **Sources** panel — add the container-side path (e.g. `/app/data/docs`). On each sync cycle, all PDF, DOCX, and TXT files in the directory and subdirectories are indexed. Documents are deduplicated by file path.

Example `compose.yml` bind mount:

```yaml
volumes:
  - ./my-notes:/app/data/docs:ro
```

Then add `/app/data/docs` in the **Local Directories** section of the Sources panel.

### Obsidian Vault

Index a local Obsidian vault by bind-mounting the vault path into the container and registering it in the **Sources** panel.

- **YAML frontmatter** — `title`, `date`, and `tags` are extracted and stored as metadata
- **Wikilinks** — `[[Note Title]]` references are resolved to their text content
- **Tags** — Obsidian `#tag` syntax is stripped to clean text before indexing

Example bind mount:

```yaml
volumes:
  - /home/user/vault:/app/data/obsidian:ro
```

Then add `/app/data/obsidian` in the **Obsidian Vaults** section of the Sources panel.

### YouTube Transcripts

Add YouTube video URLs in the **Sources** panel. Auto-captions are fetched from YouTube's timed-text API and indexed as searchable text. Videos that have no captions are skipped gracefully. No external packages — the transcript is fetched and parsed directly.

### GitHub

Index READMEs and issues from any public or private GitHub repository. Configure repos in the **Sources** panel as `owner/repo` entries.

- **PAT support** — add a personal access token in Sources for private repos or higher rate limits
- **Per-repo options** — toggle README, issues, and PRs independently

### Browser Bookmarks

Point at a Chrome or Firefox HTML bookmark export file (the standard Netscape bookmark format). Each bookmark URL is scraped and indexed. Configure the file path in the **Sources** panel.

### File Upload

Click **Upload Doc** in the `⋯` overflow menu to upload files directly from the browser. Supported formats: **PDF, DOCX, DOC, TXT, MD**. Files are deduplicated by content hash — uploading the same file twice is a no-op.

### Watch Folder

Drop files into `./data/watch/` on the host and they're picked up and indexed automatically within seconds. Same format support as file upload.

### Notes

Create and edit personal notes directly in the app — open **Notes** from the `⋯` overflow menu. Notes are stored in SQLite and indexed into Qdrant as `sourceType: "note"`, so they're searchable alongside all other sources. Use the **Notes** filter chip to search notes exclusively.

---

## Querying

Type a question in the chat input and press Enter. The app:

1. Optionally rewrites your query into a keyword-rich search query (if **Query Rewriting** is enabled in Sources)
2. Runs **two searches in parallel**: dense vector search (Qdrant) using the embedded query, and BM25 keyword search (SQLite FTS5) using the original query
3. Merges both result lists with **Reciprocal Rank Fusion (RRF)** — chunks that score well in both searches rise to the top
4. Retrieves a larger surrounding context window for each matched chunk (parent-document retrieval)
5. Streams an answer from the LLM using those chunks as context
6. Cites sources inline with `[1]`, `[2]` markers — clicking a marker scrolls to and highlights the matching source card
7. Shows clickable **source cards** below the answer — tap any card to preview the matched text

### Inline citations

The LLM is instructed to cite sources using `[N]` markers wherever it draws on indexed content. After the response streams in, each marker becomes a clickable link that jumps to and highlights the corresponding source card at the bottom of the message.

### Hybrid search

Every query runs two retrieval passes simultaneously:

- **Dense (semantic)** — the query is embedded with `nomic-embed-text` and compared against Qdrant vectors. Finds conceptually similar content even when you use different words.
- **Sparse (keyword / BM25)** — the original query is matched against a SQLite FTS5 index with Porter stemming. Finds exact names, identifiers, technical terms, and dates that embeddings tend to miss.

Results from both passes are merged with **Reciprocal Rank Fusion** (RRF, k=60): chunks that rank highly in both lists score highest, while chunks that only appear in one list are still surfaced. The combined ranking is used to select the final context window sent to the LLM.

This is the current industry baseline for RAG retrieval quality and requires no configuration — it is always on.

> **First-time upgrade note:** If you have existing indexed data from v2.0.0, click **Rebuild FTS Index** in Dev Tools once to backfill the keyword index. New ingestion is automatically dual-write going forward.

### Query rewriting

Enable **Query Rewriting** in the Sources panel. Before embedding, the LLM rewrites your question into a precise, keyword-rich search query. Useful when your phrasing is vague or conversational and you want better recall from the vector store. Query rewriting applies to the embedding path only — the original query is always sent to BM25.

### Parent-document retrieval

Chunks are embedded small for embedding precision, but when retrieved, the LLM receives a larger surrounding context window (half a chunk on each side). This improves answer quality on long documents without sacrificing retrieval accuracy.

### Source filters

Click **Filters ▾** above the chat input to expand the filter bar. Only chips for your enabled connectors are shown.

| Chip | Sources searched |
|---|---|
| All | Everything |
| Email | Gmail |
| Drive | Google Drive |
| Calendar | Google Calendar |
| RSS | RSS / Atom feeds |
| Web | Scraped web pages |
| Local | Local directory files |
| Notes | User-created notes |
| Obsidian | Obsidian vault |
| YouTube | YouTube transcripts |
| GitHub | GitHub READMEs and issues |
| Bookmarks | Scraped bookmark URLs |

### Date filter

A date filter bar sits below the source chips (also behind the **Filters ▾** toggle). Click a chip to restrict results to a recent time window:

| Chip | Window |
|---|---|
| All time | No date restriction |
| 7d | Last 7 days |
| 30d | Last 30 days |
| 3mo | Last 3 months |
| 1y | Last 12 months |

When filters are collapsed, any active non-default filter (source ≠ All, date ≠ All time) is summarised inline next to the **Filters ▾** pill.

### TopK

The **Results** selector (in the filter bar) controls how many document chunks are retrieved and sent to the LLM. Options: 4, 8, 12, 16. A higher value gives the model more context but increases latency.

### Managing sources

Click **Sources** in the header to open the connector config panel. Add or remove RSS feeds, web URLs, Obsidian vaults, YouTube URLs, GitHub repos, and bookmark file paths without editing any config files. Changes take effect on the next sync cycle.

### Connector toggles

Inside the Sources panel, toggle individual connectors on or off without removing their configuration. Disabled connectors are skipped during every sync cycle (scheduled and manual). The source filter bar automatically reflects which connectors are enabled — disabled connectors have no chip. Toggle state persists to `./data/app.db`.

### Custom system prompt

Edit the system prompt in the Sources panel. Use `{today}` as a placeholder for the current date. Click **Reset to default** to restore the original prompt.

### Chat-only mode

Toggle **No retrieval** in the Sources panel (or inline per query) to send messages directly to the LLM without searching the index. Useful for general questions where you don't want document context.

### Quick prompts

Configure shortcut chips in the Sources panel under **Quick Prompts**. They appear above the chat input and pre-fill common queries with one click.

### Sync interval

Set the background sync frequency from the Sources panel (0 = disabled, or any positive number of minutes). The change is applied immediately — no restart required.

### LLM model

The Sources panel includes a **Model** dropdown populated from Ollama's available models. Select any model you've pulled with `ollama pull <model>` and it takes effect on the next query. The choice is persisted to `./data/app.db`.

### Manual sync

Click **Sync All** in the header to immediately run all enabled connectors. Useful after adding a new source or when you want fresh data without waiting for the next scheduled sync.

### Conversation memory

Each Q&A turn is automatically added to the current session's context. Follow-up questions like "what about the one from last week?" work without repeating yourself.

- **Auto-save** — sessions are saved to SQLite (`./data/app.db`) after every response and restored on page reload.
- **History panel** — click **History** in the header to browse past sessions. Click any session to restore the full conversation. Search sessions by title or content with the search box.
- **New chat** — click **+ New Chat** inside the history panel to start a fresh session without losing the old one.
- The last 5 Q&A turns are sent as context with each query. Older turns remain visible in the UI but are not re-sent.

### Saved answers

Click the bookmark icon on any assistant response to save it to **Saved Answers** (accessible from the `⋯` overflow menu). Saved answers persist to SQLite.

### Index browser

Open **Index** from the `⋯` overflow menu to browse all indexed documents.

- **Stats bar** — chip per source type with its document count
- **Search and filter** — type to filter by title, or pick a source type from the dropdown
- **Document list** — every indexed document with source badge, title, author, date, and chunk count
- **Delete a document** — trash icon on any row removes it and all its chunks from Qdrant
- **Delete all by type** — click the × on a stat chip to bulk-remove a source type

### URL hash routing

Panels update `location.hash` — direct links like `/index.html#sources` open the correct panel, and browser back/forward navigation works as expected.

### Copy an answer

Every assistant response has a **copy** button that appears on hover. Copies the full answer text to the clipboard.

### Light mode

Click the **sun/moon** icon in the header to switch between dark and light themes. The preference is saved in localStorage.

### Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl/Cmd + K` | Focus the chat input from anywhere |
| `Esc` | Close any open panel |

### Dev Tools

Open **Dev Tools** from the `⋯` overflow menu:

- **Restart** — exit and let Docker's `restart: unless-stopped` bring the container back up with the same image. Use this after changing `appsettings.json` or environment variables.
- **Rebuild** — compile a new image from source and restart. Use this after changing C# or frontend code. The UI shows a reconnecting spinner and resumes automatically.
- **Rebuild FTS Index** — scrolls all Qdrant vectors and backfills the SQLite FTS5 keyword index. Run this once after upgrading from v2.0.0. Reports the number of chunks indexed when complete.

---

## Examples

### Example queries

**Calendar**
> What's on my calendar this week?

*You have three events this week: a dentist appointment Monday at 2pm [1], a team standup Wednesday at 10am with Sarah, James, and Kim [2], and dinner Friday at 7pm [3].*

---

**Email**
> Has anyone emailed me about the kitchen renovation quote?

*Yes — on May 8th, Riverside Contracting sent you a quote for $14,200 covering cabinet replacement and tile work [1]. They asked for a response by the 20th.*

---

**Obsidian**
> Summarise my notes on the Zettelkasten method.

*Your note "Zettelkasten Overview" (tagged #pkm #notes) describes the slip-box method as a network of atomic ideas linked by reference [1]. A follow-up note "Linking Notes" expands on how connections between cards generate emergent insights [2].*

---

**YouTube**
> What did the Andrej Karpathy video say about tokenisation?

*In the tokenisation video, Karpathy explains that BPE merges the most frequent byte pairs iteratively [1]. He notes that tokenisation is the root cause of many LLM quirks — counting letters, reversing strings, and spelling tasks all break at the token boundary [2].*

---

**Mixed sources**
> Have I discussed the house budget anywhere?

*I found references in two places: a Gmail thread from April titled "House reno budget" mentioning a $40k cap [1], and a Google Doc called "2026 Finances" that includes a home improvement line item of $35,000 [2].*

---

## GPU Acceleration

### NVIDIA

```bash
docker compose -f docker-compose.yml -f docker-compose.nvidia.yml up -d
```

### AMD (ROCm)

```bash
docker compose -f docker-compose.yml -f docker-compose.amd.yml up -d
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                      RAG-A-Muffin (.NET 8)                          │
│                                                                      │
│  ┌──────────────┐  ┌───────────────────────────┐  ┌──────────────┐  │
│  │  Chat UI     │  │  Connectors               │  │  Ingestion   │  │
│  │  SSE stream  │  │  Gmail / Drive / Calendar │  │  Chunker     │  │
│  │  Citations   │  │  RSS / Web / Local        │  │  Parent ctx  │  │
│  │  Source chips│  │  Obsidian / YouTube       │  │  Embedder    │  │
│  │  Sources mgr │  │  GitHub / Bookmarks       │  │  Dedup       │  │
│  └──────────────┘  │  File Upload / Watch      │  │  Upsert ×2   │  │
│                    └───────────────────────────┘  └──────────────┘  │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │  SQLite (./data/app.db)                                      │    │
│  │  ChatSessions · ChatMessages · Notes · Bookmarks            │    │
│  │  KV (connector config, settings) · SyncLog                  │    │
│  │  ChunksFTS (FTS5 · BM25 · porter stemming)                  │    │
│  └────────────────────────────┬─────────────────────────────────┘    │
└────────────────────────────┬──┼──────────────────────────────────────┘
                             │  │ parallel retrieval + RRF merge
                ┌────────────┘  └────────────────┐
                ▼                                ▼
          ┌───────────┐                   ┌─────────────┐
          │  Qdrant   │                   │   Ollama    │
          │  Vector   │                   │  llama3     │
          │  Database │                   │  nomic-     │
          │  (dense)  │                   │  embed-text │
          └───────────┘                   └─────────────┘
```

**Models used:**

| Role | Model |
|---|---|
| Embeddings | `nomic-embed-text` (768-dim, Int8 quantized in Qdrant) |
| Inference (streaming) | `llama3` (configurable from Sources panel) |

Both models run locally via Ollama and are pulled automatically on first start.

---

## Configuration

`appsettings.json` controls infrastructure settings. Connector sources are managed through the **Sources** UI and persisted to `./data/app.db`.

```json
{
  "Ollama": {
    "BaseUrl": "http://ollama:11434"
  },
  "Qdrant": {
    "Host": "qdrant",
    "Port": 6334
  },
  "Ingestion": {
    "IntervalMinutes": 60,
    "MaxEmailsPerSync": 100
  },
  "Connectors": {
    "Rss": { "Feeds": [] },
    "Web": { "Urls": [] },
    "Drive": { "FolderIds": [], "MaxFiles": 50 },
    "Calendar": { "DaysBack": 30, "DaysAhead": 7 }
  }
}
```

---

## Data Persistence

All persistent data lives in `./data/` on the host:

| Path | Contents |
|---|---|
| `./data/app.db` | SQLite — chat sessions, connector config, settings, sync log, notes, bookmarks |
| `./data/credentials.json` | Google OAuth client credentials (place here before first run) |
| `./data/tokens/` | Google OAuth refresh tokens |
| `./data/uploads/` | Uploaded files |
| `./data/watch/` | Watch folder (drop files here for auto-ingestion) |
| `qdrant_data` (Docker volume) | Vector embeddings |
| `ollama_data` (Docker volume) | Downloaded models |

> **Backup**: `cp data/app.db backup/app.db` is all you need. The SQLite WAL file is flushed on each write so a copy is always safe.

---

## API Reference

| Method | Path | Description |
|---|---|---|
| `GET` | `/setup/status` | Check whether a user is configured |
| `POST` | `/setup` | Set the active user (`{ "email": "you@gmail.com" }`) |
| `GET` | `/authorize?userId=` | Start Google OAuth flow |
| `GET` | `/oauth2callback` | OAuth redirect handler |
| `POST` | `/query` | One-shot RAG query |
| `POST` | `/query/stream` | Streaming RAG query (SSE) |
| `POST` | `/query/preview` | Return chunks that would be sent to the LLM without calling it |
| `POST` | `/ingest/upload` | Upload a file for ingestion |
| `GET` | `/inbox?userId=` | Manually trigger a Gmail sync |
| `GET` | `/config/connectors` | Get current connector config |
| `PUT` | `/config/connectors` | Save connector config |
| `GET` | `/config/settings` | Get current app settings |
| `PUT` | `/config/settings` | Save app settings |
| `GET` | `/config/models` | List models available in Ollama |
| `GET` | `/logs` | Fetch recent application log entries |
| `GET` | `/synclog` | Get full sync log |
| `GET` | `/synclog/latest` | Get most recent sync run per connector |
| `POST` | `/sync` | Immediately run all enabled connectors |
| `GET` | `/chats` | List all saved chat sessions |
| `GET` | `/chats/{id}` | Get a full session including all messages |
| `POST` | `/chats` | Create or update a session |
| `DELETE` | `/chats/{id}` | Delete a session |
| `GET` | `/chats/search?q=` | Full-text search across session titles and messages |
| `GET` | `/index/stats` | Total vector count and per-source-type document counts |
| `GET` | `/index/documents` | List all indexed documents (`?source=` to filter) |
| `DELETE` | `/index/documents/{id}` | Remove a document and all its chunks |
| `DELETE` | `/index/source/{type}` | Bulk-remove all documents of a source type |
| `GET` | `/index/documents/{id}/chunks` | Return all chunks for a document |
| `POST` | `/index/documents/{id}/similar` | Find semantically similar documents |
| `GET` | `/notes` | List all notes |
| `POST` | `/notes` | Create a note |
| `PUT` | `/notes/{id}` | Update a note |
| `DELETE` | `/notes/{id}` | Delete a note |
| `GET` | `/bookmarks` | List all saved answers |
| `POST` | `/bookmarks` | Save an answer |
| `DELETE` | `/bookmarks/{id}` | Delete a saved answer |
| `POST` | `/admin/restart` | Exit the process; Docker restarts the container |
| `POST` | `/admin/rebuild` | Build a new image from source, then restart |
| `POST` | `/admin/rebuild-fts` | Backfill FTS5 index from Qdrant; returns `{ "indexed": N }` |

Swagger UI is available at **http://localhost:8000/swagger** in development mode.

---

## Project Structure

```
rag-a-muffin/
├── Auth/
│   └── VendorAuth.cs               # Google OAuth (Gmail, Drive, Calendar)
├── Database/
│   └── AppDatabase.cs              # SQLite schema, migrations, connection factory
├── Models/
│   ├── SourceDocument.cs
│   ├── TextChunk.cs                # Chunk with optional ParentText context window
│   ├── EmbeddedChunk.cs
│   ├── ScoredChunk.cs
│   ├── DocumentSummary.cs
│   ├── IndexStats.cs
│   ├── ChatMessage.cs
│   ├── ChatSession.cs
│   ├── AppSettings.cs              # LLM model, system prompt, query rewriting flag
│   └── QueryRequest.cs
├── Qdrant/
│   └── QdrantInitializer.cs
├── Services/
│   ├── Interfaces/
│   │   ├── IVectorStore.cs
│   │   └── IFtsStore.cs             # BM25 / FTS5 search contract
│   ├── Connectors/
│   │   ├── GmailConnector.cs
│   │   ├── GoogleDriveConnector.cs
│   │   ├── GoogleCalendarConnector.cs
│   │   ├── RssConnector.cs
│   │   ├── WebConnector.cs
│   │   ├── LocalDirectoryConnector.cs
│   │   ├── ObsidianConnector.cs    # Vault indexing with frontmatter + wikilinks
│   │   ├── YouTubeConnector.cs     # Transcript fetch via timed-text API
│   │   ├── GitHubConnector.cs      # READMEs + issues via GitHub REST API
│   │   └── BookmarksConnector.cs   # Netscape HTML bookmark export scraper
│   ├── Extractors/
│   ├── Logging/
│   ├── ChatSessionService.cs
│   ├── ConnectorConfigService.cs
│   ├── ConnectorSyncService.cs
│   ├── SettingsService.cs
│   ├── NoteService.cs
│   ├── BookmarkService.cs
│   ├── SyncLogService.cs
│   ├── ChunkService.cs             # Word-based chunker with parent context windows
│   ├── FileWatcherService.cs
│   ├── FileIngestionService.cs
│   ├── IngestionPipeline.cs
│   ├── FtsSearchService.cs         # SQLite FTS5 BM25 keyword search
│   ├── RrfMerger.cs                # Reciprocal Rank Fusion merge utility
│   ├── RagQueryService.cs          # Hybrid retrieval, query rewriting, citations
│   ├── QdrantService.cs
│   └── UserProfileService.cs
├── wwwroot/
│   └── index.html                  # Single-page chat UI
├── Properties/
│   └── AssemblyInfo.cs             # InternalsVisibleTo for test project
├── Program.cs
└── appsettings.json

rag-a-muffin.Tests/
├── ChunkerTests.cs                 # TextChunker unit tests incl. parent window
├── ObsidianConnectorTests.cs       # Frontmatter, wikilink, tag parsing
├── ChatSessionServiceTests.cs      # CRUD + search against in-memory SQLite
├── FtsSearchServiceTests.cs        # FtsSearchService integration tests (in-memory SQLite)
└── RrfMergerTests.cs               # RRF merge unit tests
```

---

## Development

All development runs through Docker Compose — there's no need to install .NET, Qdrant, or Ollama locally.

### First build

```bash
docker compose up --build        # foreground — logs stream to terminal
docker compose up --build -d     # background — detached
```

The first run pulls Ollama models (`nomic-embed-text`, `llama3`) which takes a few minutes.

### Rebuild after code changes

Only the `api` service needs rebuilding when you change C# or frontend files:

```bash
docker compose up --build api
```

Qdrant and Ollama continue running and don't need to restart.

You can also trigger a rebuild from the browser via **Dev Tools → Rebuild** in the `⋯` overflow menu — no terminal access required.

### Run tests

```bash
dotnet test rag-a-muffin.Tests/
```

The test suite uses in-memory SQLite — no Docker or running services required. 34 tests total.

### Tail logs per service

```bash
docker compose logs -f api      # app logs
docker compose logs -f ollama   # model loading / inference
docker compose logs -f qdrant   # vector DB
```

### Stop everything

```bash
docker compose down             # stop containers, keep volumes
docker compose down -v          # stop containers and delete all data
```

---

## Troubleshooting

**Ollama models not loaded yet** — the first `docker compose up` pulls models which takes several minutes. Watch progress with:
```bash
docker compose logs -f ollama
```

**"Unverified app" warning from Google** — expected. You're using your own developer credentials. Click **Continue**.

**Drive or Calendar not syncing** — these require scopes that weren't in the original Gmail-only authorization. Sign out in the UI and re-authorize to grant all three scopes at once.

**YouTube videos not indexing** — the video has no auto-generated captions, or captions are disabled by the uploader. The connector skips these gracefully and logs the reason.

**GitHub rate limit errors** — unauthenticated requests are limited to 60/hour. Add a personal access token in the Sources panel to raise the limit.

**Port conflicts on startup** — if you have a stale stack from a previous run:
```bash
docker compose down
docker ps -a               # find any lingering containers
docker rm -f <name>
docker compose up -d
```

**Force a full reset (deletes all data):**
```bash
docker compose down -v
docker compose up --build -d
```

---

## Privacy & Security

- **No external calls** during inference — all LLM and embedding processing runs locally via Ollama.
- **Google API calls** go directly from your device to Google's servers using your own OAuth credentials. No intermediary server.
- **OAuth tokens** are stored in `./data/tokens/` on your host machine and are never included in the Docker image.
- **No telemetry** — no tracking, no analytics, no external logging.

---

**Keep your data private. Run AI locally. Stay in control.**
