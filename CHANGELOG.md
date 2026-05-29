# Changelog

All notable changes to RAG-A-Muffin are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [2.1.1] - 2026-05-29

### Fixed
- `credentials.json` is now read from `./data/credentials.json` first, falling back to the repo root. Eliminates a Docker Desktop on Windows permission error caused by single-file bind mounts with restrictive Windows ACLs. Move your existing file into `./data/` and rebuild.

---

## [2.1.0] - 2026-05-23

### Added
- **Hybrid BM25 + vector search** — every query now runs a dense vector search (Qdrant) and a BM25 keyword search (SQLite FTS5) in parallel; results are merged with Reciprocal Rank Fusion (RRF, k=60). Exact names, identifiers, and technical terms that embeddings miss are now reliably surfaced.
- **SQLite FTS5 index** — a `ChunksFTS` virtual table with Porter stemming (`porter unicode61`) is created automatically in `./data/app.db`. New ingestion writes to both Qdrant and FTS5 simultaneously; no configuration required.
- **Rebuild FTS Index** button in Dev Tools — one-click backfill from existing Qdrant data into FTS5. Required once after upgrading from v2.0.0; subsequent ingestion is automatically dual-write.
- `POST /admin/rebuild-fts` API endpoint — scrolls all Qdrant points and re-indexes them into FTS5; returns `{ "indexed": N }`.
- `RrfMerger` unit tests (7) and `FtsSearchService` integration tests (8) against in-memory SQLite — test suite now 34 tests total.

### Changed
- Query rewriting now applies to the embedding path only; the original user query is sent to BM25 to preserve exact term matching.

---

## [2.0.0] - 2026-05-20

### Added
- **Inline citations** — LLM now cites sources with `[1]`, `[2]` markers inline in its answers; clicking a marker scrolls to and highlights the corresponding source card
- **Parent-document retrieval** — chunks are still embedded small for precision, but the LLM receives a larger surrounding context window (half a chunk on each side); improves answer quality on long documents
- **Query rewriting** — optional toggle in Sources panel; before embedding, the LLM rewrites the user's question into a precise, keyword-rich search query; improves recall for vague phrasing
- **Obsidian vault connector** — bind-mount a vault path; indexes all `.md` files with YAML frontmatter parsing (title, date, tags), wikilink resolution (`[[Note]]` → text), and Obsidian-style tag stripping
- **YouTube transcript connector** — add YouTube video URLs in Sources panel; auto-captions are fetched and indexed as searchable text; videos without captions are skipped gracefully
- **GitHub connector** — index READMEs and issues from any public or private (with PAT) GitHub repo; configured as `owner/repo` entries in Sources panel
- **Browser bookmarks connector** — point at a Chrome/Firefox HTML bookmark export; each bookmark URL is scraped and indexed
- **URL hash routing** — panels now update the browser URL (`#history`, `#notes`, `#sources`, etc.); back/forward navigation and direct links to panels work as expected
- **Integration test suite** — xUnit project with 19 tests covering `TextChunker`, `ObsidianConnector` parsing, and `ChatSessionService` CRUD/search against in-memory SQLite
- Filter chips added for Obsidian, YouTube, GitHub, and Bookmarks sources

### Changed
- Prompt context limit raised from 800 to 1200 characters per chunk (parent window used when available)
- System prompt now instructs the LLM to cite sources inline with `[N]` notation

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
