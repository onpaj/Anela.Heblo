---
date: 2026-03-31
topic: Knowledge Base Chunk Detail Modal
status: approved
---

# Knowledge Base Chunk Detail Modal

## Overview

When a user asks a question or runs a semantic search in the Knowledge Base, up to 5 relevant chunks are returned. Currently, each chunk shows only a filename and a 200-character excerpt. This feature adds a modal that opens when the user clicks on a source/chunk, showing the full conversation content, its AI-generated summary, and document metadata.

## Context

- Each `KnowledgeBaseChunk` stores a whole conversation as its `Content` field. Chunks are not sub-sections of a document — each chunk IS the full conversation.
- `Summary` contains the AI-generated summary used for embedding generation.
- `KnowledgeBaseDocument` holds metadata: `Filename`, `DocumentType`, `IndexedAt`.
- The Ask tab's `SourceReference` currently lacks `ChunkId` — it must be added.
- The Search tab's `ChunkResult` already has `ChunkId`.

## Backend Changes

### Repository

Add to `IKnowledgeBaseRepository`:

```csharp
Task<KnowledgeBaseChunk?> GetChunkByIdAsync(Guid chunkId, CancellationToken ct = default);
```

Implementation in `KnowledgeBaseRepository` eagerly loads the `Document` navigation property (single JOIN query).

### Use Case: `GetChunkDetail`

Location: `Application/Features/KnowledgeBase/UseCases/GetChunkDetail/`

**Request:**
```csharp
public class GetChunkDetailRequest : IRequest<GetChunkDetailResponse>
{
    public Guid ChunkId { get; set; }
}
```

**Response:**
```csharp
public class GetChunkDetailResponse
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; }
    public DocumentType DocumentType { get; set; }
    public DateTime? IndexedAt { get; set; }
    public int ChunkIndex { get; set; }
    public string Summary { get; set; }
    public string Content { get; set; }
}
```

**Handler:** Fetches via repository. Throws `NotFoundException` if chunk does not exist.

### Controller

New action on existing `KnowledgeBaseController`:

```
GET /api/knowledge-base/chunks/{id}
→ 200 GetChunkDetailResponse
→ 404 if not found
```

### `SourceReference` Change

Add `ChunkId` to `SourceReference` in `AskQuestionResponse`:

```csharp
public class SourceReference
{
    public Guid ChunkId { get; set; }   // added
    public Guid DocumentId { get; set; }
    public string Filename { get; set; }
    public string Excerpt { get; set; }
    public double Score { get; set; }
}
```

`AskQuestionHandler` sets `ChunkId = c.ChunkId` from `ChunkResult`.

## Frontend Changes

### API Hook

New hook in `useKnowledgeBase.ts`:

```typescript
useChunkDetail(chunkId: string | null)
```

Uses React Query `useQuery` with `enabled: !!chunkId`. Fetches `GET /api/knowledge-base/chunks/{id}`.

### `ChunkDetailModal` Component

New file: `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`

Follows existing `FeedbackDetailModal` pattern:
- `fixed inset-0` backdrop, `75vw` width, `max-h-[90vh]`, scrollable body
- Escape key closes the modal

**Props:**
```typescript
interface ChunkDetailModalProps {
  chunkId: string;
  score?: number;       // passed from parent (search/ask result)
  onClose: () => void;
}
```

**Layout:**

```
┌─────────────────────────────────────────────┐
│ [Filename]                          [X]      │  ← header
├─────────────────────────────────────────────┤
│ [Conversation badge] [IndexedAt] [Score %]  │  ← meta row
│                                             │
│ Shrnutí                                     │
│ ┌─────────────────────────────────────────┐ │
│ │ <Summary text in shaded box>            │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ Obsah                                       │
│ <full Content, whitespace-pre-wrap,         │
│  scrolls within modal body>                 │
└─────────────────────────────────────────────┘
```

Loading state: skeleton placeholder while hook fetches.

### Ask Tab (`KnowledgeBaseAskTab`)

- Each source row in `SourceAccordion` gets a "Zobrazit zdroj" button.
- Local state: `selectedChunkId: string | null`, `selectedScore: number | undefined`.
- On click: set state → render `ChunkDetailModal`.
- On close: clear state.

### Search Tab (`KnowledgeBaseSearchTab`)

- Each `ChunkCard` gets a "Zobrazit zdroj" button (or the whole card becomes clickable).
- Same local state pattern as Ask tab.

## Data Flow

```
User clicks "Zobrazit zdroj"
  → selectedChunkId set in parent tab state
  → ChunkDetailModal renders
  → useChunkDetail(chunkId) fires GET /api/knowledge-base/chunks/{id}
  → Modal shows skeleton → then summary + content + metadata
User presses Escape or X
  → onClose() → selectedChunkId = null → modal unmounts
```

## Testing

**Backend:**
- `GetChunkDetailHandlerTests`: happy path, chunk not found (404)
- `KnowledgeBaseRepositoryIntegrationTests`: add `GetChunkByIdAsync` integration test
- `KnowledgeBaseControllerTests`: add endpoint test

**Frontend:**
- Unit test for `ChunkDetailModal`: renders summary, content, metadata; Escape closes
- No E2E tests required (covered by existing KB E2E scenarios)

## Out of Scope

- Navigation between chunks (prev/next) within the modal
- Editing or re-indexing from the modal
- Showing chunk index or sibling chunks
