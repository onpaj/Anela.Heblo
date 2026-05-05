# Design: Phase 1 — UI Consolidation & SharePoint Link-Back

## UX/UI Design

### Sidebar — Marketing group consolidation

The existing `marketing` section (Kalendář, Generátor letáků) is extended in-place; the standalone `knowledgebase` section is removed. End result is one collapsible Marketing group with five items in order:

```
┌─────────────────────────────┐
│ AH  Anela Heblo             │
├─────────────────────────────┤
│ ⊞  Dashboard                │
│ ...                         │
│ ▼ 📣 Marketing              │  ← Megaphone icon, expanded
│    Kalendář                 │
│    Generátor letáků         │
│    Generátor článků         │  ← added (was unreachable)
│    Poradenství (KB)         │  ← moved from knowledgebase section
│    Feedback                 │  ← conditional: managers only
│ ...                         │
│ ▶ 🤖 Automatizace           │
│                             │  ← Knowledgebase section removed
└─────────────────────────────┘
```

The `knowledgebase` section (Database icon, "Poradenství" + conditional "Feedback") is deleted. `Database` is removed from the lucide-react import block (verify no other in-file usage before removing). The Feedback item in the merged section is conditioned on `hasRole('knowledge_base_manager') || hasRole('leaflet_manager') || hasRole('article_generator')`.

Collapsed state behaviour is unchanged — icon-only, clicking expands the sidebar.

### Marketing Feedback stub page

Minimal placeholder with standard page wrapper. No interactive elements.

```
┌────────────────────────────────────────────┐
│  Feedback                                  │  ← <h1>
│                                            │
│  Přehled zpětné vazby bude dostupný        │  ← <p>
│  po dokončení integrace.                   │
└────────────────────────────────────────────┘
```

Wrapper: `<div className="p-6">`. No card, no table, no spinner. Page lives at `/marketing/feedback` inside the existing `<AuthGuard>` + `<Layout>` tree.

### Chunk detail modal — SharePoint link row

The link is inserted between the meta row and the Summary block in both modals. It is omitted entirely when `sourcePath` is null, empty, or starts with `upload/`.

**KB ChunkDetailModal** (current layout is at `ChunkDetailModal.tsx:59-93`):

```
┌──────────────────────────────────────────────────────┐
│ filename.docx                               [×]      │
├──────────────────────────────────────────────────────┤
│  Dokument  •  Indexováno: 1. 5. 2025  •  87%        │  ← meta row (unchanged)
│  Otevřít v SharePoint ↗                             │  ← NEW: only when https://
│                                                      │
│  SHRNUTÍ                                             │
│  ┌────────────────────────────────────────────────┐  │
│  │ ...                                            │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  OBSAH                                               │
│  ...                                                 │
└──────────────────────────────────────────────────────┘
```

**LeafletChunkDetailModal** (`LeafletChunkDetailModal.tsx:59-88`) — identical layout change; the only structural difference from KB modal is the absence of the `documentType` pill and `score` in its meta row (current state, unchanged).

Link element spec:
- Tag: `<a>` with `target="_blank" rel="noopener noreferrer"`
- Label: `Otevřít v SharePoint` + trailing `<ExternalLink className="w-3 h-3" />`
- Classes: `inline-flex items-center gap-1 text-xs text-blue-600 hover:underline`
- Rendered inside the `data && (...)` block, immediately after the `<div className='flex items-center gap-3 ...'>` meta row

## Component Design

### Frontend

#### `frontend/src/utils/sharepointLink.ts` (NEW)

Pure synchronous function with no side effects, no React imports.

```ts
export function getSharePointLink(sourcePath: string | null | undefined): string | null
```

Returns the input verbatim when it starts with `"https://"`, `null` in all other cases (null, undefined, empty string, `"upload/..."` prefix).

No default export. Imported by both modals and testable in isolation.

#### `frontend/src/utils/sharepointLink.test.ts` (NEW)

Unit tests covering: `null`, `undefined`, `""`, `"upload/abc/file.pdf"`, `"https://sharepoint.example.com/doc"`.

#### `frontend/src/pages/MarketingFeedbackPage.tsx` (NEW)

Default export. Stateless functional component. No props, no hooks, no data fetching.

```tsx
export default function MarketingFeedbackPage() {
  return (
    <div className="p-6">
      <h1 ...>Feedback</h1>
      <p ...>Přehled zpětné vazby bude dostupný po dokončení integrace.</p>
    </div>
  );
}
```

#### `frontend/src/components/Layout/Sidebar.tsx` (MODIFIED)

**Change 1 — `navigationSections` array:**
- Extend the `marketing` section (id `"marketing"`) items array from 2 items to 5 (or 4 + conditional spread).
- Remove the `knowledgebase` section object entirely (currently lines 299–314).

**Change 2 — imports:**
- Remove `Database` from the lucide-react import (confirmed single usage on line 20 as the knowledgebase section icon).
- No other import changes required (`Megaphone` and `ExternalLink` are already imported).

No changes to the rendering logic, toggle behaviour, or collapsed state handling.

#### `frontend/src/App.tsx` (MODIFIED)

Add import of `MarketingFeedbackPage` and register route `/marketing/feedback` inside the existing `<AuthGuard>` + `<Layout>` block, alongside the existing `/knowledge-base/feedback` route (lines ~466-474).

#### `frontend/src/components/knowledge-base/ChunkDetailModal.tsx` (MODIFIED)

Add import of `getSharePointLink` from `../../utils/sharepointLink` and `ExternalLink` from `lucide-react`.

Insert the link element after the closing tag of the meta row `<div>` (currently line 74), before the Summary `<div>`:

```tsx
{getSharePointLink(data.sourcePath) && (
  <a
    href={getSharePointLink(data.sourcePath)!}
    target="_blank"
    rel="noopener noreferrer"
    className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline"
  >
    Otevřít v SharePoint
    <ExternalLink className="w-3 h-3" />
  </a>
)}
```

#### `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx` (MODIFIED)

Identical change as KB modal above; insertion point is after the meta row `<div>` (currently line 66).

### Backend

#### `GetChunkDetailRequest.cs` — `GetChunkDetailResponse` class (MODIFIED)

File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`

Add one property to the `GetChunkDetailResponse` class:

```csharp
public string? SourcePath { get; set; }
```

#### `GetChunkDetailHandler.cs` (MODIFIED)

File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`

Add `SourcePath = chunk.Document.SourcePath,` to the response object initialiser (after `Content = chunk.Content,`, currently line 34).

#### `GetLeafletChunkDetailRequest.cs` — `GetLeafletChunkDetailResponse` class (MODIFIED)

File: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailRequest.cs`

Same additive change:

```csharp
public string? SourcePath { get; set; }
```

#### `GetLeafletChunkDetailHandler.cs` (MODIFIED)

File: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs`

Add `SourcePath = chunk.Document.SourcePath,` to the response object initialiser.

#### Repositories — no changes

`KnowledgeBaseRepository.GetChunkByIdAsync` and `LeafletRepository.GetChunkByIdAsync` already include `.Include(c => c.Document)`. No repository modifications needed.

### Test files (MODIFIED — existing test classes extended)

**`GetChunkDetailHandlerTests.cs`** — add three facts:
1. `SourcePath` equals document's SharePoint URL when document has `https://` path.
2. `SourcePath` equals synthetic path verbatim when document has `upload/...` path.
3. `SourcePath` is `""` (empty string) when document `SourcePath` defaults to `string.Empty`.

**`GetLeafletChunkDetailHandlerTests.cs`** — same three facts.

**Frontend test files (NEW):**
- `frontend/src/utils/sharepointLink.test.ts` — five test cases covering all input categories.
- `frontend/src/components/knowledge-base/ChunkDetailModal.test.tsx` — three cases (SharePoint URL renders link; `upload/...` hides link; `null` hides link).
- `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.test.tsx` — same three cases.

## Data Schemas

### Backend DTO changes (additive)

**`GetChunkDetailResponse`** — after change:

```csharp
public class GetChunkDetailResponse : BaseResponse
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; }
    public DateTime? IndexedAt { get; set; }
    public int ChunkIndex { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? SourcePath { get; set; }           // NEW
}
```

**`GetLeafletChunkDetailResponse`** — after change:

```csharp
public class GetLeafletChunkDetailResponse : BaseResponse
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DateTime? IndexedAt { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? SourcePath { get; set; }           // NEW
}
```

### API response shapes (JSON)

`GET /api/knowledge-base/chunks/{id}` — additive change:
```json
{
  "chunkId": "...",
  "documentId": "...",
  "filename": "example.docx",
  "documentType": "Document",
  "indexedAt": "2025-05-01T10:00:00Z",
  "chunkIndex": 0,
  "summary": "...",
  "content": "...",
  "sourcePath": "https://anelacz.sharepoint.com/..."
}
```

`sourcePath` is `null` or an empty string when the document has no SharePoint origin; a `"upload/{guid}/{filename}"` string when manually uploaded; a full `https://` URL when sourced from SharePoint/OneDrive.

`GET /api/leaflet/chunks/{id}` — identical additive change to its response shape.

### Frontend TypeScript shapes

Auto-generated by the OpenAPI client on `npm run build`. After regeneration the generated interfaces gain:

```ts
// In api-client.ts (auto-generated, do not edit manually)
interface GetChunkDetailResponse {
  // ...existing fields...
  sourcePath?: string;
}

interface GetLeafletChunkDetailResponse {
  // ...existing fields...
  sourcePath?: string;
}
```

### `SourcePath` semantics (existing convention, unchanged)

| Value pattern | Origin | UI behaviour |
|---|---|---|
| `https://...` | SharePoint / OneDrive ingestion | Render as clickable link |
| `upload/{guid}/{filename}` | Manual upload pipeline | Omit link (helper returns `null`) |
| `""` (empty string default) | No source set | Omit link (falsy check catches it) |
| `null` | Defensive DTO nullability | Omit link |
