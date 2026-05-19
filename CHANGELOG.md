# Changelog

All notable changes to RAG-A-Muffin are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [1.5.0] - 2026-05-19

### Added
- Quick-prompt bar above the chat input — configurable chip shortcuts stored in settings
- New-content badge on the Sync All button — amber dot when new sync results exist since last page load
- Digest shortcut that pre-fills a "summarise what's new in 24h" query with automatic date filter
- `note` chip in the source filter bar for querying notes independently

### Changed
- `AppSettings` extended with `SystemPrompt`, `NoRetrieval`, and `QuickPrompts` fields

---

## [1.4.0] - 2026-05-19

### Added
- Custom system prompt — editable textarea in Sources panel; `{today}` placeholder; reset to default
- No-retrieval (chat-only) mode — global toggle in Sources panel and per-query inline toggle
- `POST /query/preview` — returns chunks that would be sent to the LLM without calling it
- `QueryRequest.NoRetrieval` flag passed through from frontend to query engine

---

## [1.3.0] - 2026-05-19

### Added
- `POST /index/documents/{id}/similar` — find semantically similar documents from a seed document
- `GET /chats/search?q=` — full-text search across session titles and message content
- Search box in the History panel with live debounced filtering

---

## [1.2.0] - 2026-05-19

### Added
- Notes panel with full CRUD editor; notes stored in SQLite and indexed into Qdrant as `sourceType: "note"`; deleted from index on removal
- Bookmarks — 🔖 button on every assistant answer saves it to a Saved Answers panel
- `GET/POST/PUT/DELETE /notes` and `GET/POST/DELETE /bookmarks` endpoints
- `NoteService` and `BookmarkService` backed by SQLite `Notes` and `Bookmarks` tables

---

## [1.1.0] - 2026-05-19

### Added
- `GET /index/documents/{id}/chunks` — returns all chunks for a document (text + length)
- `GET /synclog` and `GET /synclog/latest` endpoints
- Sync log section in the Status panel showing last run time, outcome, and document count per connector
- `SyncLog` table in SQLite; `SyncLogService` records every connector run

---

## [1.0.0] - 2026-05-19

### Added
- SQLite database (`./data/app.db`) replacing all flat JSON file storage
- `ChatSessions` and `ChatMessages` tables with WAL mode and CASCADE delete
- `KV` table for connector config and app settings (JSON blobs, hot in-memory reads)
- `SyncLog`, `Notes`, and `Bookmarks` tables
- One-time startup migration imports existing `connectors.json`, `settings.json`, and `chats/*.json`

### Changed
- `ChatSessionService` rewritten from file I/O to SQLite
- `ConnectorConfigService` rewritten to use KV table
- `SettingsService` rewritten to use KV table
- Docker volume backup simplified to `cp data/app.db backup/`

---

## [0.9.0] - 2026-05-15

### Added
- Light/dark mode toggle persisted to `localStorage`
- Copy button on each assistant message
- Export chat as Markdown from the History panel
- `Cmd/Ctrl+K` focuses chat input; `Esc` closes all panels

### Changed
- Mobile layout with `@media (max-width: 620px)` breakpoints across all panels
- Source preview highlights query terms in matched chunks

---

## [0.8.0] - 2026-05-15

### Added
- Gmail attachment indexing (PDF, DOCX) with stable document IDs derived from message ID and filename
- Gmail label filter — configurable label list stored in connector config
- Local directory connector — bind-mount a host path, auto-ingest recursively on sync

---

## [0.7.0] - 2026-05-15

### Added
- Model selector in Sources panel — fetches available models from Ollama `/api/tags`
- Configurable sync interval set from the Sources panel, applied dynamically
- Per-connector enable/disable toggles for Gmail, Drive, Calendar, RSS, and Web

### Changed
- Selected model and sync interval persisted to settings; applied on next query without restart

---

## [0.6.0] - 2026-05-15

### Added
- Date-range filter chips (All time / 7d / 30d / 3mo / 1y) mapped to Qdrant `DatetimeRange` filter
- TopK selector (4 / 8 / 12 / 16) in the UI; sent per-query; default 8

---

## [0.5.0] - 2026-05-15

### Added
- Multi-turn conversation history carried into the LLM prompt
- Chat sessions persisted to SQLite
- Chat history browser with restore and delete
- New Chat button

---

## [0.4.0] - 2026-05-15

### Added
- Document browser panel — lists all indexed documents with source type, title, date, and chunk count; searchable and filterable
- Delete document by ID or bulk-delete by source type
- Qdrant stats widget — total vectors broken down by source type
- Re-index a specific web URL or local file without clearing others
- Index health badge in the header showing total document count

---

## [0.3.0] - 2026-05-15

### Added
- Sources panel for managing RSS feeds and web URLs
- Status panel for connector health
- Live log viewer panel
- Sync All button — manually trigger all connectors
- Dev Tools panel — Restart and Rebuild from the browser
- Toast notifications
- Person-aware search ("from X", "to Y" query extraction)
- Volume-mounted data persistence (`./data/`)

### Changed
- Citations sorted newest-first

---

## [0.2.0] - 2026-05-15

### Added
- Google Drive connector (Docs, Sheets, PDF, DOCX)
- Google Calendar connector
- RSS / Atom feed connector
- Web page scraper connector
- File upload endpoint (PDF, DOCX, TXT, MD)
- Watch folder — drop files into `./data/watch/` for automatic ingestion
- Email parser strips replies, signatures, and junk
- Per-document deduplication by content hash and source ID

---

## [0.1.0] - 2026-05-15

### Added
- Gmail ingestion (inbox + sent) via OAuth
- Word-based text chunking with configurable size and overlap
- Embedding via Ollama (`nomic-embed-text`, 768-dim)
- Qdrant vector store with scalar quantization (Int8)
- Streaming RAG query over SSE
- Source-type filtering
- Citation deduplication and display
