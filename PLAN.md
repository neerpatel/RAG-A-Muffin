# RAG-A-Muffin — Feature Plan

## Completed

### Phase 1 — Core RAG
- [x] Gmail ingestion (inbox + sent, OAuth)
- [x] Text chunking (word-based, configurable size/overlap)
- [x] Embedding via Ollama (nomic-embed-text, 768-dim)
- [x] Qdrant vector store with scalar quantization (Int8)
- [x] Streaming RAG query (SSE, llama3)
- [x] Source-type filtering in queries
- [x] Citation deduplication + display (clickable cards)

### Phase 2 — Multi-Source Connectors
- [x] Google Drive connector (Docs, Sheets, PDF, DOCX)
- [x] Google Calendar connector
- [x] RSS / Atom feed connector
- [x] Web page scraper connector
- [x] File upload endpoint (PDF, DOCX, TXT, MD)
- [x] Watch folder (drop files, auto-ingest)
- [x] Email parser (strip replies, signatures, junk)
- [x] Per-document deduplication by content hash / source ID

### Phase 3 — Polish & Ops
- [x] Sources panel — manage RSS feeds and web URLs from the UI
- [x] Status panel — connector health, sync intervals
- [x] Logs panel — in-memory log viewer
- [x] Sync All button — manually trigger all connectors
- [x] Dev Tools panel — Restart and Rebuild from the browser
- [x] Citation sort (newest-first)
- [x] Person-aware search ("from X", "to Y" query extraction)
- [x] Toast notifications with correct z-index
- [x] Volume-mounted data persistence (`./data/` on host)
- [x] Docker project name locked (`name: rag-a-muffin`)
- [x] Helper-container rebuild (survives container self-exit)

---

## Proposed

### Phase 4 — Document Management & Visibility ✓

The index is currently a black box. You can put things in but can't see or manage what's there.

- [x] **Document browser** — panel listing all indexed documents with source type, title, date, chunk count. Searchable/filterable.
- [x] **Delete document** — remove a specific document and all its chunks from Qdrant by document ID.
- [x] **Delete by source type** — bulk-wipe all documents of a given type (e.g. clear all RSS chunks).
- [x] **Qdrant stats widget** — total vectors, breakdown by source type, shown in Index panel header.
- [x] **Re-index document** — force a fresh scrape/sync for a specific web URL or file without clearing others.
- [x] **Index health indicator** — header badge showing total document count; turns amber if index is empty.

---

### Phase 5 — Conversation Memory ✓

Each query is currently stateless. Multi-turn context would make the assistant far more useful.

- [x] **Session conversation history** — carry the last N turns of Q&A into the LLM prompt so follow-up questions work ("what about the one from last week?" refers to the previous answer).
- [x] **Persist chat sessions to disk** — save conversations to `./data/chats/` as JSON; load on page refresh.
- [x] **Chat history browser** — panel listing past sessions; click to restore, with delete per session.
- [x] **Clear session** — "new chat" link in input bar and button in history panel starts a fresh conversation.

---

### Phase 6 — Search Quality ✓

- [ ] **Hybrid search** — combine Qdrant vector search with BM25 sparse vectors for better keyword recall. Especially useful for names, product IDs, and exact phrases. *(skipped — requires Qdrant sparse vector collection rework, breaking change)*
- [ ] **Cross-encoder re-ranking** — after vector retrieval, re-rank the top-K chunks with a local cross-encoder model (e.g. `bge-reranker` via Ollama) before sending to the LLM. *(skipped — no Ollama cross-encoder support)*
- [x] **Date-range filter** — date chips in the UI (All time / 7d / 30d / 3mo / 1y) mapped to `DateTimeOffset` range parameters passed through to Qdrant's `DatetimeRange` filter on the `publishedAt` field.
- [ ] **Query rewriting** — optionally rephrase the user's question before embedding (improves recall on vague queries). *(skipped — adds latency with uncertain benefit)*
- [x] **Larger context window** — topK selector (4 / 8 / 12 / 16 / 24) in the UI; sent per-query. Default remains 8.

---

### Phase 7 — Model Flexibility ✓

Models are currently hardcoded. Users have different hardware and preferences.

- [x] **Model selector UI** — fetches available models from Ollama `/api/tags` via `GET /config/models`; user picks LLM from a dropdown in the Sources panel.
- [x] **Persist model choice** — selected model saved to `./data/settings.json` via `PUT /config/settings`; applied on next query dynamically (no restart needed).
- [ ] **Chunk size tuning** — expose chunk size and overlap as UI sliders in Settings. *(skipped — changing chunk size on existing index would require re-indexing all documents)*
- [x] **Configurable sync interval** — sync interval set from the Sources panel; persisted to `connectors.json`; applied dynamically without restart.
- [x] **Per-connector toggles** — enable/disable Gmail, Drive, Calendar, RSS, and Web individually from the Sources panel. Disabled connectors are skipped on each sync cycle.

---

### Phase 8 — Additional Connectors ✓

- [x] **Gmail attachment indexing** — when an email has attachments, download and index them (PDF, DOCX) via `Users.Messages.Attachments.Get`; stable document ID from `SHA256("gmail-attachment:{messageId}/{filename}")`.
- [x] **Gmail label filter** — configurable label list (e.g. `INBOX`, `SENT`, `STARRED`) stored in `connectors.json`; `FetchByLabelsAsync` deduplicates by message ID across label calls.
- [ ] **Obsidian vault** — watch a local Obsidian vault directory; index markdown notes with frontmatter metadata. *(skipped)*
- [ ] **Notion** — sync pages from a Notion workspace via the Notion API. *(skipped)*
- [x] **Local directory connector** — index an arbitrary host path (bind-mounted into the container); `LocalDirectoryConnector` scans recursively, uses existing PDF/DOCX/TXT extractors, deduplicates by path hash.

---

### Phase 9 — UX & Mobile ✓

- [x] **Mobile layout** — `@media (max-width: 620px)` breakpoints: scrollable filter bars, smaller header buttons, reduced padding.
- [x] **Light mode** — sun/moon toggle in header; `--bg`, `--surface`, `--text`, etc. overridden via `:root.light`; preference persisted to localStorage.
- [x] **Keyboard shortcuts** — `Cmd/Ctrl+K` to focus the chat input from anywhere; `Esc` closes all open panels.
- [x] **Answer copy button** — "copy" button on each assistant message (visible on hover); shows "copied!" for 2s after click.
- [x] **Source preview improvement** — highlight query terms in the matched chunk preview using frontend word-splitting (no backend changes needed).
- [x] **Export chat** — download the current session as a Markdown file from the History panel.
- [ ] **Inline citations** — mark citation numbers `[1]`, `[2]` inline in the streamed answer. *(skipped — requires streaming protocol changes)*

---

### Phase 10 — SQLite Data Layer

Currently all structured application data lives in flat JSON files (`connectors.json`, `settings.json`, one file per chat session). This works for a single user but has real limitations: no queryability, no transactions (power-loss on a Pi can corrupt a partial write), and each new feature (notes, bookmarks, sync log) adds yet another JSON file to manage.

Replacing this with an embedded **SQLite** database keeps everything local and homelab-friendly — no new Docker service, just a single `app.db` file in `./data/`. EF Core with the SQLite provider gives migrations, LINQ queries, and transactions with minimal boilerplate.

**What moves into SQLite:**
- Chat sessions and messages (currently `./data/chats/*.json`)
- Connector config (currently `./data/connectors.json`)
- App settings (currently `./data/settings.json`)
- Sync run log (new — needed for Phase 11)
- Notes (new — needed for Phase 12)
- Bookmarks (new — needed for Phase 12)
- Quick-prompt templates (new — needed for Phase 15)

**What stays as files:**
- `./data/tokens/` — Google OAuth tokens; expected on-disk by the auth flow

**Migration:** On first boot after the upgrade, the app detects existing JSON files and imports them into SQLite before removing the originals. No data loss for existing users.

**Implementation steps:**
- [x] Add `Microsoft.Data.Sqlite` package; define `AppDatabase` with raw SQL schema (KV, ChatSessions, ChatMessages, SyncLog, Notes, Bookmarks).
- [x] Replace `ChatSessionService` (file-backed) with SQLite repository; added full-text search via `GET /chats/search?q=`.
- [x] Replace `ConnectorConfigService` (file-backed) with SQLite KV table; keep the same in-memory singleton pattern for hot reads, write-through to DB.
- [x] Replace `SettingsService` (file-backed) with SQLite; extended `AppSettings` with `SystemPrompt`, `NoRetrieval`, `QuickPrompts`.
- [x] One-time `JsonMigrationService` built into `AppDatabase` constructor; marks migration complete via KV flag.
- [x] Docker volume docs updated — backup is now `cp data/app.db backup/`.

*This phase is foundational infrastructure. Phases 11–15 assume it is complete.*

---

### Phase 11 — Index Transparency

The index is now browsable, but still opaque in terms of freshness and internal structure. *The sync log item requires Phase 10 (SQLite); the others are independent.*

- [x] **Source sync log** — Status panel shows per-connector: last run time, outcome (ok / error), and document count. Persisted to SQLite via `SyncLogService`.
- [x] **Chunk viewer** — `GET /index/documents/{id}/chunks` returns all chunks for a document (text + length). Frontend endpoint available.
- [ ] **Indexed-at column** — show "synced X days ago" on each index document row. *(deferred — requires stamping metadata during ingestion)*
- [ ] **Duplicate detection** — flag documents with near-identical title + source. *(deferred)*

---

### Phase 12 — Personal Notes

A lightweight first-party source for thoughts you want to query alongside your other data. *Requires Phase 10 (SQLite) for notes and bookmark storage.*

- [x] **Notes connector** — Notes panel with full CRUD editor; notes stored in SQLite and indexed into Qdrant as `sourceType: "note"`. Appears in the source filter chip bar.
- [x] **Bookmarks** — 🔖 button on every assistant answer saves it to a Bookmarks panel (Saved Answers). Stored in SQLite.
- [x] **Note editor** — dedicated Notes panel with list pane + editor pane; title + body; save, delete, new.

---

### Phase 13 — Discovery & Exploration

Move beyond "question → answer" toward browsing and serendipitous discovery.

- [x] **"Find similar"** — `POST /index/documents/{id}/similar` re-embeds the seed chunk and returns nearest neighbours. Available as an API endpoint.
- [x] **Chat session search** — search box in the History panel; `GET /chats/search?q=` queries SQLite across session titles and message content.
- [x] **Recent activity** — sync log table (Phase 10) backs the per-connector last-run results shown in the Status panel.

---

### Phase 14 — Prompt & Retrieval Control

Give power users more control over how the LLM is used without breaking the defaults.

- [x] **Custom system prompt** — editable textarea in Sources panel; persisted to SQLite; "Reset to default" button; `{today}` placeholder.
- [x] **No-retrieval (chat-only) mode** — global toggle in Sources panel + inline toggle above chat input; bypasses vector search entirely.
- [x] **Retrieval preview** — `POST /query/preview` returns the chunks that would be sent to the LLM without calling it.

---

### Phase 15 — Notification & Alerting

Passive awareness of what's new without having to ask.

- [x] **New-content indicator** — amber dot on Sync All button when new sync results exist since last page load (localStorage timestamp vs. `SyncLog`).
- [x] **Digest prompt shortcut** — `digestShortcut()` function pre-fills a "summarise what's new in 24h" query with automatic date filter.
- [x] **Quick-prompt bar** — configurable chip shortcuts above the chat input; managed in Sources panel under "Quick Prompts"; stored in SQLite settings.

---

## Notes

- Phases are suggestions, not fixed sprints. Pick any feature from any phase.
- Qdrant's scroll API can back the document browser (Phase 4) without schema changes.
- Hybrid search (Phase 6) requires adding Qdrant sparse vector support — non-trivial but high-value.
- Conversation history (Phase 5) is the single biggest UX win relative to effort.
- All proposed phases respect the core principle: no external calls, no telemetry, everything on-device.
