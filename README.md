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

A local, privacy-first RAG (Retrieval-Augmented Generation) system that runs on a Raspberry Pi or any homelab device. Connect your Gmail, Google Drive, Google Calendar, RSS feeds, websites, and documents, then ask questions about your life in a streaming chat UI — with everything staying on your hardware.

## Why RAG-A-Muffin?

- **100% private** — your data never leaves your device. No cloud uploads, no third-party APIs processing your personal information.
- **Local AI** — uses [Ollama](https://ollama.com) for on-device LLM inference and embeddings. Models run entirely locally.
- **Multi-source** — Gmail, Drive, Calendar, RSS feeds, web scraping, local directories, file uploads, and a watch folder all feed into one searchable index.
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
# Place your credentials.json here (see Google Setup)
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
4. Rename the file to `credentials.json` and place it in the repo root (next to `docker-compose.yml`).

### Step 5 — Authorize in the UI

Start the app and open **http://localhost:8000**. Enter your Gmail address and click **Connect Gmail**. A Google sign-in tab opens — complete sign-in there, then click **I've signed in** in the app.

The app requests read-only access to Gmail, Drive, and Calendar in one authorization. If you previously authorized with Gmail only, you'll need to re-authorize to unlock Drive and Calendar sync.

> Google may show an "unverified app" warning since these are your own developer credentials. Click **Continue** to proceed.

---

## Data Sources

All sources funnel into the same vector store. Use the **source filter chips** in the chat UI to scope queries to a specific source type.

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

Add feed URLs through the **Sources** panel (gear icon in the header) or in `appsettings.json`:

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

Pages are indexed once per URL. To re-index a changed page, remove the entry and re-add it (the URL-based document ID will be treated as new).

### Local Directory

Index any directory bind-mounted into the container. Configure paths through the **Sources** panel — add the container-side path (e.g. `/app/data/docs`). On each sync cycle, all PDF, DOCX, and TXT files in the directory (and subdirectories) are indexed. Documents are deduplicated by file path, so only new or changed files create new index entries.

Example `compose.yml` bind mount:

```yaml
volumes:
  - ./my-notes:/app/data/docs:ro
```

Then add `/app/data/docs` in the **Local Directories** section of the Sources panel.

### File Upload

Click **Upload Doc** in the header to upload files directly from the browser. Supported formats: **PDF, DOCX, DOC, TXT, MD**. Files are deduplicated by content hash — uploading the same file twice is a no-op.

### Watch Folder

Drop files into `./data/watch/` on the host and they're picked up and indexed automatically within seconds. Same format support as file upload.

---

## Querying

Type a question in the chat input and press Enter. The app:

1. Embeds your query with `nomic-embed-text`
2. Searches the vector store for the most relevant chunks
3. Streams an answer from `llama3` using those chunks as context
4. Shows clickable **source cards** below the answer — tap any card to preview the matched text

### Source filters

Use the chips above the input to scope results:

| Chip | Sources searched |
|---|---|
| All | Everything |
| Email | Gmail |
| Docs | Uploaded files and watch folder |
| Drive | Google Drive |
| Calendar | Google Calendar |
| RSS | RSS / Atom feeds |
| Web | Scraped web pages |
| Local | Local directory files |

### Date filter

A second filter bar sits below the source chips. Click a date chip to restrict results to a recent time window:

| Chip | Window |
|---|---|
| All time | No date restriction |
| 7d | Last 7 days |
| 30d | Last 30 days |
| 3mo | Last 3 months |
| 1y | Last 12 months |

The filter maps directly to Qdrant's `publishedAt` field — only documents indexed within that window are considered.

### TopK

The **Results** selector (next to the date chips) controls how many document chunks are retrieved and sent to the LLM. Options: 4, 8, 12, 16, 24. A higher value gives the model more context but increases latency.

### Managing RSS feeds and web URLs

Click the **Sources** icon in the header to open the connector config panel. Add or remove RSS feeds and web URLs without editing any config files. You can either click **+ Add** after typing a URL, or just type the URL and click **Save changes** — the pending value is auto-added before saving. Changes take effect on the next sync cycle.

### Connector toggles

Inside the Sources panel, toggle individual connectors on or off without removing their configuration. Disabled connectors are skipped during every sync cycle (scheduled and manual). Toggle state persists to `./data/app.db`.

### Sync interval

Set the background sync frequency from the Sources panel (0 = disabled, or any positive number of minutes). The change is applied immediately to the running sync loop — no restart required.

### LLM model

The Sources panel includes a **Model** dropdown populated from Ollama's available models. Select any model you've pulled with `ollama pull <model>` and it takes effect on the next query. The choice is persisted to `./data/app.db`.

### Manual sync

Click **Sync All** in the header to immediately run all connectors (Gmail, Drive, Calendar, RSS, web). Useful after adding a new source or when you want fresh data without waiting for the next scheduled sync.

### Conversation memory

Each Q&A turn is automatically added to the current session's context. Follow-up questions like "what about the one from last week?" or "can you summarise that differently?" work without repeating yourself.

- **Auto-save** — sessions are saved to SQLite (`./data/app.db`) after every response and restored on page reload.
- **History panel** — click the **History** (clock) icon to browse past sessions. Click any session to restore the full conversation.
- **New chat** — click **+ New Chat** inside the history panel, or the **new chat** link in the input bar, to start a fresh session without losing the old one.
- The last 5 Q&A turns (10 messages) are sent as context with each query. Older turns remain visible in the UI and saved to disk, but are not re-sent to avoid prompt bloat.

### Index browser

Click the **Index** (database) icon in the header to open the document index panel. The badge on the button shows the total number of indexed documents at a glance, and turns amber when the index is empty.

Inside the panel:

- **Stats bar** — shows a chip per source type with its document count (gmail, drive, rss, etc.)
- **Search and filter** — type to filter by title, or pick a source type from the dropdown to narrow the list
- **Document list** — every indexed document with its source badge, title, author, date, and chunk count
- **Delete a document** — click the trash icon on any row to remove that document and all its chunks from Qdrant
- **Delete all by type** — click the × on a stat chip to bulk-remove every document of that source type

### Copy an answer

Every assistant response has a **copy** button that appears on hover. Click it to copy the full answer text to the clipboard. The button briefly shows "copied!" to confirm.

### Light mode

Click the **sun/moon** icon in the header to switch between dark and light themes. The preference is saved in localStorage and restored on next visit.

### Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl/Cmd + K` | Focus the chat input from anywhere |
| `Esc` | Close any open panel (Sources, Status, History, Index, Logs, Dev) |

### Dev Tools

Click the **Dev** (wrench) icon in the header to open the Dev Tools panel. From here you can:

- **Restart** — exit and let Docker's `restart: unless-stopped` bring the container back up with the same image. Use this after changing `appsettings.json` or environment variables.
- **Rebuild** — compile a new image from source and restart. Use this after changing C# or frontend code. The UI shows a reconnecting spinner and resumes automatically when the new container is ready.

---

## Examples

### What the UI looks like

```
┌──────────────────────────────────────────────────────────────────────┐
│ 🧁 rag-a-muffin    ● local  [will@gmail.com ✕]                       │
│ [Status] [Sources] [Logs] [Upload Doc] [Sync All] [Index 42] [Hist] [Dev] │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  YOU                                                                 │
│  ╔════════════════════════════════════════════╗                      │
│  ║ What's on my calendar this week?           ║                      │
│  ╚════════════════════════════════════════════╝                      │
│                                                                      │
│  MUFFIN                                                              │
│  ┌────────────────────────────────────────────┐                      │
│  │ You have three events this week:           │                      │
│  │                                            │                      │
│  │ • Monday 2pm — Dentist appointment         │                      │
│  │ • Wednesday 10am — Team standup with       │                      │
│  │   Sarah, James, and Kim                    │                      │
│  │ • Friday 7pm — Dinner at The Capital       │                      │
│  └────────────────────────────────────────────┘                      │
│                                                                      │
│  SOURCES                                                             │
│  ┌─────────────────────┐  ┌─────────────────────┐                   │
│  │ Dentist Appointment │  │ Team Standup        │                   │
│  │ May 19, 2026        │  │ May 21, 2026        │                   │
│  │ ↗ CALENDAR          │  │ ↗ CALENDAR          │                   │
│  └─────────────────────┘  └─────────────────────┘                   │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│ source  [All] [Email] [Docs] [Drive] [Calendar] [RSS] [Web]          │
│ time    [All time] [7d] [30d] [3mo] [1y]    Results [8 ▾]           │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ Ask about your docs, emails, anything...                      ↵  │ │
│ └──────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

### Example queries

**Calendar**
> What's on my calendar this week?

*You have three events this week: a dentist appointment Monday at 2pm, a team standup Wednesday at 10am with Sarah, James, and Kim, and dinner Friday at 7pm.*

---

**Email**
> Has anyone emailed me about the kitchen renovation quote?

*Yes — on May 8th, Riverside Contracting sent you a quote for $14,200 covering cabinet replacement and tile work. They asked for a response by the 20th.*

---

**Google Drive**
> Summarise my Q1 planning document.

*Your Q1 plan (last edited March 3rd) outlines three priorities: launching the new onboarding flow, migrating the database to Postgres, and reducing support ticket volume by 20%. The owner is listed as you, with a review date of April 1st.*

---

**Mixed sources**
> Have I discussed the house budget anywhere?

*I found references in two places: a Gmail thread from April with your partner titled "House reno budget" mentioning a $40k cap, and a Google Doc called "2026 Finances" that includes a home improvement line item of $35,000.*

---

**RSS**
> What's the latest in AI this week?

*Based on your feeds, the main stories this week are Meta's release of Llama 4, a new benchmark showing o3 outperforming humans on ARC-AGI, and a Wired piece on the energy cost of large-scale inference.*

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
┌─────────────────────────────────────────────────────────────────┐
│                    RAG-A-Muffin (.NET 8)                        │
│                                                                  │
│  ┌──────────────┐  ┌─────────────────┐  ┌──────────────────┐   │
│  │  Chat UI     │  │  Connectors     │  │  Ingestion       │   │
│  │  (SSE stream)│  │  Gmail          │  │  Chunker         │   │
│  │  Source chips│  │  Drive          │  │  Embedder        │   │
│  │  Citations   │  │  Calendar       │  │  Deduplication   │   │
│  │  Sources mgr │  │  RSS / Web      │  │  Vector upsert   │   │
│  └──────────────┘  │  File / Watch   │  └──────────────────┘   │
│                    └─────────────────┘                          │
└────────────────────────────┬────────────────────────────────────┘
                             │
              ┌──────────────┴──────────────┐
              ▼                             ▼
        ┌───────────┐               ┌─────────────┐
        │  Qdrant   │               │   Ollama    │
        │  Vector   │               │  llama3     │
        │  Database │               │  nomic-     │
        │           │               │  embed-text │
        └───────────┘               └─────────────┘
```

**Models used:**

| Role | Model |
|---|---|
| Embeddings | `nomic-embed-text` (768-dim, Int8 quantized in Qdrant) |
| Inference (streaming) | `llama3` |

Both models run locally via Ollama and are pulled automatically on first start.

---

## Configuration

`appsettings.json` controls infrastructure settings. Connector sources (RSS feeds, web URLs) can also be managed through the **Sources** UI, which persists to `./data/connectors.json`.

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
    "Rss": {
      "Feeds": []
    },
    "Web": {
      "Urls": []
    },
    "Drive": {
      "FolderIds": [],
      "MaxFiles": 50
    },
    "Calendar": {
      "DaysBack": 30,
      "DaysAhead": 7
    }
  }
}
```

---

## Data Persistence

All persistent data lives in `./data/` on the host:

| Path | Contents |
|---|---|
| `./data/app.db` | SQLite database — chat sessions, connector config, settings, sync log, notes, bookmarks |
| `./data/tokens/` | Google OAuth refresh tokens |
| `./data/uploads/` | Uploaded files |
| `./data/watch/` | Watch folder (drop files here for auto-ingestion) |
| `qdrant_data` (Docker volume) | Vector embeddings |
| `ollama_data` (Docker volume) | Downloaded models |

> **Backup**: `cp data/app.db backup/app.db` is all you need. The SQLite WAL file is flushed on each write so a copy is always safe. Legacy JSON files (`connectors.json`, `settings.json`, `chats/`) are imported automatically on first boot and can be removed after migration.

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
| `POST` | `/ingest/upload` | Upload a file for ingestion |
| `GET` | `/inbox?userId=` | Manually trigger a Gmail sync |
| `GET` | `/config/connectors` | Get current RSS / web connector config |
| `PUT` | `/config/connectors` | Save RSS / web connector config |
| `GET` | `/logs` | Fetch recent application log entries |
| `POST` | `/sync` | Immediately run all connectors (same as **Sync All** button) |
| `GET` | `/chats` | List all saved chat sessions (id, title, date, message count) |
| `GET` | `/chats/{id}` | Get a full session including all messages |
| `POST` | `/chats` | Create or update a session |
| `DELETE` | `/chats/{id}` | Delete a session |
| `GET` | `/index/stats` | Total vector count and per-source-type document counts |
| `GET` | `/index/documents` | List all indexed documents (`?source=gmail` to filter by type) |
| `DELETE` | `/index/documents/{id}` | Remove a document and all its chunks by document ID |
| `DELETE` | `/index/source/{type}` | Bulk-remove all documents of a given source type |
| `GET` | `/config/settings` | Get current app settings (active LLM model) |
| `PUT` | `/config/settings` | Save app settings; takes effect on the next query |
| `GET` | `/config/models` | List models available in Ollama (for the model selector) |
| `POST` | `/admin/restart` | Exit the process; Docker restarts the container with the current image |
| `POST` | `/admin/rebuild` | Build a new image from source, then restart |

Swagger UI is available at **http://localhost:8000/swagger** in development mode.

---

## Project Structure

```
rag-a-muffin/
├── Auth/
│   └── VendorAuth.cs          # Google OAuth (Gmail, Drive, Calendar)
├── Models/                    # Shared data models
│   ├── SourceDocument.cs      # Common document model for all connectors
│   ├── EmbeddedChunk.cs       # Vector store payload
│   ├── ScoredChunk.cs         # Search result
│   ├── DocumentSummary.cs     # Per-document metadata for the index browser
│   ├── IndexStats.cs          # Total vectors + per-source-type counts
│   ├── ChatMessage.cs         # Single conversation turn (role + content)
│   ├── ChatSession.cs         # Saved conversation with messages
│   ├── AppSettings.cs         # LLM model preference
│   └── QueryRequest.cs        # Query + source type filter + history + date range
├── Qdrant/
│   └── QdrantInitializer.cs   # Collection + index setup
├── Services/
│   ├── Interfaces/            # IConnector, IVectorStore, IRagQueryService, …
│   ├── Connectors/            # GmailConnector, GoogleDriveConnector,
│   │                          # GoogleCalendarConnector, RssConnector,
│   │                          # WebConnector, LocalDirectoryConnector
│   ├── Extractors/            # PdfExtractor, DocxExtractor, PlainTextExtractor
│   ├── Logging/               # InMemoryLogBuffer + ILoggerProvider
│   ├── ChatSessionService.cs      # Save/load chat sessions from ./data/chats/
│   ├── ConnectorConfigService.cs  # Live RSS/web config (persisted to connectors.json)
│   ├── ConnectorSyncService.cs    # BackgroundService: dynamic interval, connector toggles
│   ├── SettingsService.cs         # App settings singleton (LLM model, persisted to settings.json)
│   ├── FileWatcherService.cs      # BackgroundService: watches ./data/watch/
│   ├── FileIngestionService.cs    # Upload handler
│   ├── IngestionPipeline.cs       # Chunk → embed → upsert
│   ├── RagQueryService.cs         # Embed query → vector search → LLM → stream
│   ├── QdrantService.cs           # IVectorStore implementation
│   └── UserProfileService.cs      # Singleton: active user identity
├── wwwroot/
│   └── index.html             # Single-page chat UI
├── Program.cs                 # Service registration + minimal API endpoints
└── appsettings.json
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

You can also trigger a rebuild from the browser via the **Dev → Rebuild** button — no terminal access required.

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

**Port conflicts on startup** — if you have a stale stack from a previous run using a different project name, you may see `port is already allocated` errors. Stop and remove all old containers first:
```bash
docker compose down        # removes rag-a-muffin project containers
docker ps -a               # find any lingering containers from old project names
docker rm -f <name>        # remove them
docker compose up -d
```

**Check which process is using a port:**
```bash
# Linux / macOS
lsof -i :8000

# Windows
netstat -ano | findstr :8000
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
