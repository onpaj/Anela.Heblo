---
process: module-leaflet
kind: module
module: leaflet
summary: AI generator of Czech product leaflets (Generátor letáků) that combines Knowledge Base facts with the tone of past Anela leaflets, plus the library of example leaflets it learns style from.
owns: []
verified_at: "5e993f9e2"
related:
  - job-leaflet-ingestion
  - flow-leaflet-generation
---

# Leaflet generator (Generátor letáků)

## Purpose
Helps marketing write product leaflets (letáky) faster. A user enters a topic, chooses who it is
for (end customer or B2B) and how long it should be, and the module writes a first draft in Czech
Markdown. Product facts are taken from the company Knowledge Base (Poradenství); the writing style
imitates a library of past Anela leaflets that marketing maintains. The draft is copied out by
hand — the module publishes nothing and touches no ERP or e-shop data.

## Users & screens
| Where | Who | What |
|---|---|---|
| `/leaflet-generator` tab **Generovat** | page needs `marketing.leaflet.read`; generating needs `marketing.leaflet.write` | Form (topic ≤ 200 chars, audience, length), result with copy button, 1–5 rating of precision and style + comment |
| `/leaflet-generator` tab **Dokumenty** | read; delete needs write | Library of example leaflets: filter by filename/status/type, sort, open first chunk (text + AI summary), delete |
| `/leaflet-generator` tab **Nahrát soubor** | write only | Upload `.pdf` / `.docx` / `.txt` / `.md` example leaflets |
| `/marketing/feedback` tab **Letáky** | page gated by `Marketing_Article` write; list API needs leaflet write | Overview of generations and their ratings |
| MCP tool `GenerateLeaflet` | `marketing.leaflet.read` | Same generation from Claude |

Sidebar label: "Generátor letáků" (Marketing). Feature in the access matrix: `Marketing_Leaflet`.

## Processes
- `job-leaflet-ingestion` — example leaflets from SharePoint `/AI/Leaflets/Inbox` (Hangfire
  `leaflet-ingestion`, every 15 min) or manual upload → text chunks + AI summary + embedding in the
  leaflet library; SharePoint file moved to `/AI/Leaflets/Archived`.
- `flow-leaflet-generation` — topic → query expansion → vector search in KB + leaflet library →
  two Claude calls (facts outline, Czech copy) → logged in `LeafletGenerations`, rated by the author.

Plain CRUD/read (no separate doc): document list and content-type filter
(`GET /api/leaflet/documents`, `/documents/content-types`), chunk detail (`GET /api/leaflet/chunks/{id}`),
generation detail (`GET /api/leaflet/generations/{id}`), document delete (DB only, covered in
`job-leaflet-ingestion`).

## Data owned
| Table | One row = |
|---|---|
| `LeafletDocuments` | One example leaflet file: filename, source path (SharePoint `webUrl` or `upload/{guid}/{name}`), MIME type, SHA-256 `ContentHash`, word count, Graph `DriveId`/`GraphItemId`, status `processing`/`indexed`/`failed`, `IngestedAt`/`IndexedAt` (UTC, timestamp without time zone) |
| `LeafletChunks` | One ~800-word window of a leaflet: text, LLM keyword summary, word count, `vector(1536)` embedding (HNSW cosine index); cascade-deleted with its document |
| `LeafletGenerations` | One successful generation: topic, audience, length, final Markdown, KB/leaflet source counts, duration, user, and the optional one-time rating (precision 1–5, style 1–5, comment) |

No caches, no blob containers. The uploaded/SharePoint file itself is not stored by Heblo.

## External systems
| System | Direction | What |
|---|---|---|
| Microsoft Graph (SharePoint drive from `Leaflet:OneDriveFolderMappings`) | read + move | List inbox children, download file, PATCH-move to archive folder (app-only token) |
| Anthropic Claude | call | Query expansion (`claude-haiku-4-5-20251001`), Stage 1 + 2 generation (`claude-sonnet-4-6`), per-chunk summaries at ingestion (default chat model = `KnowledgeBase:ChatModel`) |
| OpenAI embeddings | call | `text-embedding-3-large` at 1536 dimensions, for chunks and topics |

## Dependencies
- **Knowledge-base module** — the fact source. Leaflet defines `ILeafletKnowledgeSource`; the KB
  module implements it (`KnowledgeBaseLeafletSourceAdapter`) as a cosine search over
  `KnowledgeBaseChunks`. KB content and its ingestion are documented by that module. The KB
  config section also decides whether the shared OneDrive service is real Graph or a mock.
- **Shared RAG infrastructure** (`Application/Shared/Rag`): OneDrive service, text extractors
  (PDF / Word / plain text), word-window chunker, query expander.
- **Feedback page** (`/marketing/feedback`) reads `LeafletGenerations` via the leaflet API
  alongside KB, article and Smartsupp feedback.
- No other module reads leaflet tables.

## Known quirks
- A leaflet whose indexing fails once is never retried — next run treats it as a duplicate by
  hash and archives it (details in `job-leaflet-ingestion`).
- Per-chunk LLM summaries are generated and stored but used neither for search nor generation.
- Readers see the generate form but get a generic failure (endpoint needs write); MCP lets
  readers generate and skips the 200-character topic limit (see `flow-leaflet-generation`).
- If `KnowledgeBase:OneDriveFolderMappings` is empty, the leaflet job runs against a mock
  OneDrive and never sees files, regardless of the Leaflet config.
- Facts may come from generic model knowledge when the KB has no match above 0.55 similarity.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` — DI registration
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletOptions.cs` — all settings and prompts
- `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs` — REST endpoints and permissions
- `backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs` — MCP tool
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/` — entities and repository interfaces
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/` — EF configs, raw-SQL vector search
- `frontend/src/features/leaflet-generator/` — page and tabs
- `docs/features/leaflet-generator.md` — original feature design
