# Changelog

All notable changes to RAG-A-Muffin are documented here.

---

## 2026-05-19

### Phase 10 — SQLite Data Layer
- Replaced all flat JSON file storage with a single embedded SQLite database (`./data/app.db`)
- Chat sessions and messages migrated to normalized `ChatSessions` / `ChatMessages` tables (WAL mode, CASCADE delete)
- Connector config and app settings stored as JSON blobs in a `KV` table (hot reads stay in-memory)
- One-time startup migration imports existing `connectors.json`, `settings.json`, and `chats/*.json` automatically
- New `SyncLog` table records every connector run (connector name, outcome, document count, timestamp)
- New `Notes` and `Bookmarks` tables for Phase 12

### Phase 11 — Index Transparency
- `GET /index/documents/{id}/chunks` — chunk viewer endpoint; expands document into individual text chunks
- Sync log section added to the Status panel (latest run per connector with outcome + document count)
- `GET /synclog` and `GET /synclog/latest` endpoints

### Phase 12 — Personal Notes & Bookmarks
- **Notes** — full CRUD editor in a new Notes panel; notes indexed into Qdrant as `sourceType: "note"`; deletable from the editor (removes from index too)
- **Bookmarks** — save any assistant answer via the 🔖 button on each response; Saved Answers panel lists all bookmarks with delete
- Notes and Saved buttons added to the header

### Phase 13 — Discovery & Exploration
- `POST /index/documents/{id}/similar` — find semantically similar documents from a seed document's embedding
- `GET /chats/search?q=` — full-text search across session titles and message content
- History panel now has a live search box that filters sessions as you type
- Sync log feeds the Status panel with per-connector last-run results

### Phase 14 — Prompt & Retrieval Control
- **Custom system prompt** — editable textarea in Sources panel; `{today}` placeholder supported; "Reset to default" button
- **No-retrieval mode** — global toggle in Sources panel + per-query inline toggle above the chat input; bypasses vector search entirely
- **Retrieval preview** — `POST /query/preview` returns the chunks that would be sent to the LLM without calling it

### Phase 15 — Notification & Alerting
- **New-content badge** — amber dot on Sync All button when new sync results exist since last page load
- **Quick-prompt bar** — configurable prompt shortcuts above the chat input (managed in Sources panel); click a chip to pre-fill the input
- Quick prompts stored in SQLite settings alongside other app settings

### Other changes
- `note` chip added to the source filter bar (queries Qdrant for `sourceType: "note"`)
- `AppSettings` model extended with `SystemPrompt`, `NoRetrieval`, `QuickPrompts`
- `QueryRequest` extended with `NoRetrieval` flag
- `IVectorStore` extended with `GetChunksAsync`

---

## 2026-05-15

### Phase 9 — UX & Mobile
- Mobile layout with `@media (max-width: 620px)` breakpoints
- Light/dark mode toggle persisted to `localStorage`
- `Cmd/Ctrl+K` focuses chat input; `Esc` closes all panels
- Copy button on each assistant message
- Source preview highlights query terms
- Export chat as Markdown from the History panel

### Phase 8 — Additional Connectors
- Gmail attachment indexing (PDF, DOCX) with stable document IDs
- Gmail label filter (configurable via Sources panel)
- Local directory connector — bind-mount a host path, auto-ingest on sync

### Phase 7 — Model Flexibility
- Model selector in Sources panel (fetches from Ollama `/api/tags`)
- Persisted model choice
- Configurable sync interval
- Per-connector enable/disable toggles

### Phase 6 — Search Quality
- Date-range filter chips (All time / 7d / 30d / 3mo / 1y)
- TopK selector (4 / 8 / 12 / 16)

### Phase 5 — Conversation Memory
- Multi-turn conversation history carried into LLM prompt
- Chat sessions persisted to SQLite
- Chat history browser with restore and delete
- New Chat button

### Phase 4 — Document Management
- Document browser with search/filter
- Delete document or bulk-delete by source type
- Qdrant stats widget (total vectors by source type)
- Re-index a specific web URL or local file
- Index health badge in header

### Phase 3 — Polish & Ops
- Sources panel for managing RSS feeds and web URLs
- Status panel for connector health
- Live log viewer
- Sync All button
- Dev Tools panel (Restart / Rebuild from browser)
- Citation sort (newest-first)
- Person-aware search ("from X", "to Y")
- Toast notifications
- Volume-mounted data persistence (`./data/`)

### Phase 2 — Multi-Source Connectors
- Google Drive connector (Docs, Sheets, PDF, DOCX)
- Google Calendar connector
- RSS / Atom feed connector
- Web page scraper connector
- File upload endpoint
- Watch folder (auto-ingest on file drop)
- Email parser (strip replies, signatures, junk)
- Per-document deduplication by content hash / source ID

### Phase 1 — Core RAG
- Gmail ingestion (inbox + sent, OAuth)
- Text chunking (word-based, configurable)
- Embedding via Ollama (nomic-embed-text, 768-dim)
- Qdrant vector store with scalar quantization (Int8)
- Streaming RAG query (SSE)
- Source-type filtering
- Citation deduplication and display
