# Design: Leaflet Generator (Two-Stage RAG)

## UX/UI Design

### Page Layout

Route: `/leaflet-generator`. Opens inside the existing app shell (top bar + sidebar). Content area follows the standard `p-3 md:p-4` padding pattern on a `bg-gray-50` background.

The page uses a two-panel layout on desktop (form left, result right) that collapses to a stacked single-column on mobile.

```
┌─────────────────────────────────────────────────────────────────┐
│  Top bar (existing)                                             │
├──────────┬──────────────────────────────────────────────────────┤
│ Sidebar  │  Generátor letáků                                    │
│          │  ─────────────────────────────────────────────────── │
│ ...      │                                                       │
│ Marketing│  ┌─── LeafletForm ──────────────────────────────┐    │
│  Generátor│  │                                              │    │
│  letáků  │  │  Téma *                                       │    │
│          │  │  ┌────────────────────────────────────────┐   │    │
│          │  │  │ Bisabolol pro citlivou pleť             │   │    │
│          │  │  └────────────────────────────────────────┘   │    │
│          │  │  42 / 200 znaků                               │    │
│          │  │                                               │    │
│          │  │  Cílová skupina *      Délka *                │    │
│          │  │  ┌──────────────┐     ┌──────────────────┐   │    │
│          │  │  │ Koncový záka▼│     │ Střední (~400 sl▼│   │    │
│          │  │  └──────────────┘     └──────────────────┘   │    │
│          │  │                                               │    │
│          │  │  ┌──────────────────────────────┐            │    │
│          │  │  │  ✦ Generovat leták           │            │    │
│          │  │  └──────────────────────────────┘            │    │
│          │  └──────────────────────────────────────────────┘    │
│          │                                                       │
│          │  ┌─── LeafletResult ────────────────────────────┐    │
│          │  │                                               │    │
│          │  │  [Copy]  [Regenerovat]          (loading...) │    │
│          │  │  ─────────────────────────────────────────── │    │
│          │  │  ## Bisabolol – přírodní péče o citlivou pleť│    │
│          │  │  …rendered markdown…                         │    │
│          │  │                                               │    │
│          │  └──────────────────────────────────────────────┘    │
└──────────┴──────────────────────────────────────────────────────┘
```

### Sidebar Entry

New collapsible "Marketing" section is added directly below the last existing section. It follows the same expand/collapse pattern (ChevronRight → ChevronDown) as other sidebar sections.

```
Marketing                         ▼
  📄  Generátor letáků
```

Icon: `FileText` from lucide-react. Section label: `"Marketing"`. Item label: `"Generátor letáků"`. Route: `/leaflet-generator`. No role gate — accessible to all authenticated users (mirrors Knowledge Base).

### Component Hierarchy

```
LeafletGeneratorPage
├── LeafletForm
│   ├── <textarea> topic (200-char counter below)
│   ├── <select> audience
│   ├── <select> length
│   └── <button> generate (disabled while loading)
└── LeafletResult          (hidden until first successful generation)
    ├── <button> copy      (copies content to clipboard)
    ├── <button> regenerate (re-submits same form values)
    ├── loading skeleton   (shown during in-flight request)
    └── <ReactMarkdown>    (renders final markdown)
```

### Key Interactions

**Generating:**
1. User fills in topic (required, 1–200 chars), audience (required), length (required).
2. Submit button shows `"Generuji…"` with a spinner; all form fields are disabled while in flight.
3. On success: `LeafletResult` appears below (or to the right on ≥ md breakpoint) with rendered markdown.
4. On 422 (both retrieval sides empty): display an inline warning banner inside `LeafletResult`: `"Znalostní báze toto téma zatím nepokrývá. Zkuste širší formulaci."` — no toast, no modal.
5. On 4xx/5xx other: red inline error below the form button: `"Generování se nezdařilo. Zkuste to prosím znovu."`.

**Copying:**
- `navigator.clipboard.writeText(content)` on click.
- Button label toggles to `"Zkopírováno!"` for 2 s, then reverts.

**Regenerating:**
- Re-fires the same `POST /api/leaflet/generate` with the current form values.
- Previous result is replaced by the loading skeleton immediately.

**Character counter:**
- Below the topic textarea: `{n} / 200 znaků` in `text-sm text-gray-500`.
- Turns `text-red-500` when `n > 200` (textarea blocks submission via HTML `maxLength`).

### Form Controls

| Field | Control | Options (Czech labels) |
|---|---|---|
| Téma | `<textarea rows={3}>` | free text, maxLength=200 |
| Cílová skupina | `<select>` | `Koncový zákazník`, `B2B` |
| Délka | `<select>` | `Krátký (~200 slov)`, `Střední (~400 slov)`, `Dlouhý (~700 slov)` |

Default selections on mount: `Koncový zákazník`, `Střední (~400 slov)`.

---

## Component Design

### Frontend Components

#### `LeafletGeneratorPage` (`frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`)
Route container. Holds form state (topic, audience, length) and generation result via React Query mutation. Passes callbacks down to `LeafletForm` and result content to `LeafletResult`. On ≥ md: renders form and result side-by-side (`flex gap-6`). On mobile: stacked (`flex-col`).

**State:**
- `topic: string` — controlled textarea value
- `audience: AudienceType` — controlled select
- `length: LeafletLength` — controlled select
- `mutation: UseMutationResult<GenerateLeafletResponse>` — React Query mutation for `POST /api/leaflet/generate`

#### `LeafletForm` (`frontend/src/features/leaflet-generator/LeafletForm.tsx`)
Presentational. Receives `topic`, `audience`, `length`, `isLoading`, `onSubmit`, and individual field setters as props. Renders the form and character counter. Does not own state.

**Props interface:**
```typescript
interface LeafletFormProps {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
  isLoading: boolean;
  onTopicChange: (v: string) => void;
  onAudienceChange: (v: AudienceType) => void;
  onLengthChange: (v: LeafletLength) => void;
  onSubmit: () => void;
  error: string | null;
}
```

#### `LeafletResult` (`frontend/src/features/leaflet-generator/LeafletResult.tsx`)
Displays the generated markdown or a loading skeleton. Owns the copy-to-clipboard timer state.

**Props interface:**
```typescript
interface LeafletResultProps {
  content: string | null;
  isLoading: boolean;
  onRegenerate: () => void;
}
```

Renders nothing when `content === null && !isLoading`.

### Backend Components

#### `LeafletController` (`API/Controllers/LeafletController.cs`)
Single action: `POST /api/leaflet/generate`. Sends `GenerateLeafletRequest` via MediatR. Returns `200 GenerateLeafletResponse` on success, `400`/`422`/`502` ProblemDetails on error. Decorated with `[Authorize]`.

#### `GenerateLeafletHandler` (`Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`)
Orchestrates two-stage generation. Dependencies: `IEmbeddingGenerator`, `IKnowledgeBaseRepository`, `ILeafletRepository`, `IChatClient`, `IOptions<LeafletOptions>`, `ILogger`. Returns `GenerateLeafletResponse { Content }`.

**Key behaviour:**
- Single embedding call for the topic; reused across both retrieval stages.
- Stage 1 failure (LLM error) short-circuits; Stage 2 proceeds with empty leaflet context if repo is cold.
- When both KB and leaflet retrievals return zero above-threshold chunks: throws `UnprocessableEntityException` → controller maps to 422.
- All external calls wrapped in `Activity` spans for OTel.

#### `LeafletIndexingService` (`Application/Features/Leaflet/Services/LeafletIndexingService.cs`)
Flat pipeline: `LeafletChunker.Chunk(text)` → `IEmbeddingGenerator.GenerateAsync(batchOfChunkTexts)` → `ILeafletRepository.AddChunksAsync(chunks)`. Sets `document.WordCount` to sum of chunk word counts. No chunk summarization.

**Interface:**
```csharp
public interface ILeafletIndexingService
{
    Task IndexAsync(string text, LeafletDocument document, CancellationToken ct = default);
}
```

#### `LeafletChunker` (`Application/Features/Leaflet/Services/LeafletChunker.cs`)
Word-boundary chunker. Reads `ChunkSizeWords` (800) and `ChunkOverlapWords` (80) from `IOptions<LeafletOptions>`. Returns `IReadOnlyList<string>`. Entirely independent of `DocumentChunker`.

#### `LeafletIngestionJob` (`Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`)
Implements `IRecurringJob`. `Metadata.JobName = "leaflet-ingestion"`. Polls `/Leaflets/Inbox`, dispatches `IndexLeafletRequest` per file via MediatR, archives on success (including duplicates), leaves in Inbox on error with `Error` log.

#### `IndexLeafletHandler` (`Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs`)
Dedup logic: SHA-256 hash lookup → skip (still archive). Source-path collision → delete old, re-index. Resolves extractor from `IEnumerable<IDocumentTextExtractor>`. Delegates to `ILeafletIndexingService`. Single `SaveChangesAsync` transaction per document.

#### `LeafletRepository` (`Persistence/Features/Leaflet/LeafletRepository.cs`)
Implements `ILeafletRepository`. EF Core for document CRUD; raw `NpgsqlCommand` with `Pgvector.Vector` for `AddChunksAsync` and `SearchSimilarAsync` (mirrors `KnowledgeBaseRepository`). `LeafletChunkConfiguration` calls `builder.Ignore(x => x.Embedding)`.

#### `LeafletTools` (`API/MCP/Tools/LeafletTools.cs`)
Single MCP tool: `GenerateLeaflet`. Dispatches `GenerateLeafletRequest` via MediatR. Catches all exceptions and rethrows as `McpException` with a generic message (no internal details exposed).

**Tool signature:**
```csharp
[McpServerTool]
[Description("Generate a product leaflet using two-stage RAG. Returns polished markdown.")]
public async Task<string> GenerateLeaflet(
    [Description("Topic or product name, max 200 characters")] string topic,
    [Description("Target audience: EndConsumer or B2B")] string audience,
    [Description("Output length: Short (~200 words), Medium (~400 words), Long (~700 words)")] string length,
    CancellationToken ct)
```

---

## Data Schemas

### Database Tables

#### `LeafletDocuments`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK |
| `Filename` | `text` | NOT NULL |
| `SourcePath` | `text` | NOT NULL, UNIQUE INDEX |
| `ContentType` | `text` | NOT NULL |
| `ContentHash` | `text` | NOT NULL, non-unique INDEX |
| `IngestedAt` | `timestamptz` | NOT NULL |
| `WordCount` | `int` | NOT NULL |

#### `LeafletChunks`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK |
| `DocumentId` | `uuid` | FK → `LeafletDocuments.Id` ON DELETE CASCADE |
| `ChunkIndex` | `int` | NOT NULL |
| `Content` | `text` | NOT NULL |
| `WordCount` | `int` | NOT NULL |
| `Embedding` | `vector(1536)` | NOT NULL — added via raw SQL migration, ignored by EF |

**HNSW index** (same parameters as KB):
```sql
CREATE INDEX ix_leaflet_chunks_embedding ON "LeafletChunks"
USING hnsw ("Embedding" vector_cosine_ops)
WITH (m = 16, ef_construction = 64);
```

**Dedup notes:**
- No DB-level unique constraint on `ContentHash` (dedup enforced in `IndexLeafletHandler`).
- Unique index on `SourcePath` ensures `GetBySourcePathAsync` is O(log n) and re-index-on-path-collision is safe.

### API Shapes

#### `POST /api/leaflet/generate`

**Request:**
```json
{
  "topic": "Bisabolol pro citlivou pleť",
  "audience": "EndConsumer",
  "length": "Medium"
}
```

| Field | Type | Constraints |
|---|---|---|
| `topic` | `string` | Required, 1–200 chars, no control characters except whitespace |
| `audience` | `AudienceType` enum | Required. Values: `EndConsumer` (0), `B2B` (1) |
| `length` | `LeafletLength` enum | Required. Values: `Short` (0), `Medium` (1), `Long` (2) |

**Response 200:**
```json
{
  "content": "## Bisabolol – přírodní péče...\n\n..."
}
```

**Response 400** (validation failure):
```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "topic": ["The topic field is required."] }
}
```

**Response 422** (both retrieval sides empty — KB and leaflet store):
```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "Knowledge Base does not yet cover this topic; try a broader phrasing."
}
```

**Response 502** (transient upstream failure after one retry):
```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Bad Gateway",
  "status": 502,
  "detail": "An upstream service is temporarily unavailable. Please try again."
}
```

### `LeafletOptions` Configuration Shape

```json
{
  "Leaflet": {
    "DriveId": "<required — SharePoint drive ID>",
    "InboxPath": "/Leaflets/Inbox",
    "ArchivedPath": "/Leaflets/Archived",
    "ChunkSizeWords": 800,
    "ChunkOverlapWords": 80,
    "KbTopK": 8,
    "LeafletTopK": 5,
    "MinSimilarityScore": 0.55,
    "ChatModel": "claude-sonnet-4-6",
    "ChatMaxTokens": 2048,
    "EmbeddingModel": "<must match KB store embedding model>",
    "IngestionCronExpression": "*/15 * * * *",
    "ShortWordTarget": 200,
    "MediumWordTarget": 400,
    "LongWordTarget": 700,
    "Stage1SystemPrompt": "<configurable — see appsettings>",
    "Stage2SystemPrompt": "<configurable — see appsettings>"
  }
}
```

Required fields (validated at startup via `ValidateOnStart`): `DriveId`, `InboxPath`, `ArchivedPath`, `ChatModel`, `EmbeddingModel`.

### Stage 1 Prompt Placeholders

| Placeholder | Resolved from |
|---|---|
| `{topic}` | `request.Topic` (user message, never system) |
| `{audience}` | Enum label: `"Koncový zákazník"` / `"B2B"` |
| `{length}` | Word target from `LeafletOptions` (e.g., `"~400 slov"`) |
| `{kbContext}` | Concatenated KB chunk content above `MinSimilarityScore`; empty string on sparse retrieval |

### Stage 2 Prompt Placeholders

| Placeholder | Resolved from |
|---|---|
| `{leafletContext}` | Concatenated leaflet chunk content above `MinSimilarityScore`; empty string on cold-start |
| `{coldStart}` | `"true"` / `"false"` — signals prompt to use neutral marketing register when `"true"` |
| `{audience}` | Same as Stage 1 |
| `{length}` | Same as Stage 1 |

Stage 2 user message = Stage 1 LLM output (the factual outline).
