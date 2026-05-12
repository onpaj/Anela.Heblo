# Phase 1 — UI Consolidation & SharePoint Link-Back Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate the three RAG features (Knowledge Base, Leaflet Generator, Article Generator) under a single Marketing sidebar group, add a stub Marketing Feedback page, and surface a clickable SharePoint link in chunk detail modals when the document was sourced from SharePoint.

**Architecture:** Additive backend changes — `SourcePath` is added to two existing response DTOs and populated in two existing MediatR handlers (no repository, persistence, or controller changes; both repositories already eager-load `Document`). Frontend adds a stateless `getSharePointLink` helper, a stub page, a sidebar restructure, and inserts a small link element into two chunk detail modals. The TS hook interfaces (`GetChunkDetailResponse`, `GetLeafletChunkDetailResponse`) in `api/hooks/` are **hand-maintained**, not auto-generated — they must be edited manually to match the C# DTO change.

**Tech Stack:** .NET 8 (MediatR Vertical Slice, EF Core), xUnit + Moq + FluentAssertions, React 18 + TypeScript, Tailwind, react-router, lucide-react, Jest + React Testing Library.

---

## Important context discovered before writing

These observations correct or supplement the spec/arch-review:

1. **Spec claim that `Sidebar.tsx` has two sections with `id: 'marketing'` is false.** Verified at `frontend/src/components/Layout/Sidebar.tsx`: only one `marketing` section (lines 140–149) and a separate `knowledgebase` section (lines 299–314). No `TrendingUp` import, no `Campaigns` item. FR-2 from the spec has nothing to act on.
2. **Sidebar path uses capital `Layout`:** `frontend/src/components/Layout/Sidebar.tsx`. The arch-review document used lowercase `layout` — use capital.
3. **Response DTOs are co-located in the request files**, not separate `*Response.cs` files. Edit `GetChunkDetailRequest.cs` and `GetLeafletChunkDetailRequest.cs`.
4. **Domain `SourcePath` is non-nullable `string` defaulting to `string.Empty`** (entity `KnowledgeBaseDocument.SourcePath`). The DTO field is declared `string?` for forward flexibility, but assignment will produce `""` rather than `null` for documents with no source set. The frontend helper's falsy check (`if (!sourcePath)`) handles `""` correctly.
5. **The TS interfaces consumed by the chunk detail hooks are NOT auto-generated.** `GetChunkDetailResponse` is hand-defined at `frontend/src/api/hooks/useKnowledgeBase.ts:127` and `GetLeafletChunkDetailResponse` at `frontend/src/api/hooks/useLeaflet.ts:43`. The OpenAPI build step regenerates `frontend/src/api/generated/api-client.ts`, but the hooks don't use those generated types. **The hand-defined interfaces must be edited manually** — this is captured as Task 4.
6. **Existing tests in `GetChunkDetailHandlerTests.cs` use raw xUnit `Assert.*`**, while `GetLeafletChunkDetailHandlerTests.cs` uses FluentAssertions. Match the existing style of each file when adding new tests.
7. **Repositories already eager-load `Document`** (verified by arch-review). No repository changes are needed.
8. **`/articles` route already exists** in `App.tsx` (line 474). No new route needed for it.
9. **Leave `/knowledge-base/feedback` and `KnowledgeBaseFeedbackPage` untouched.** Spec section 4 of the arch-review confirms this is out of scope; Phase 4 will reconcile.

---

## File Structure

**Files created:**
- `frontend/src/utils/sharepointLink.ts` — pure helper exporting `getSharePointLink`
- `frontend/src/utils/sharepointLink.test.ts` — Jest unit tests for the helper (5 cases)
- `frontend/src/pages/MarketingFeedbackPage.tsx` — minimal stub page
- `frontend/src/features/leaflet-generator/__tests__/LeafletChunkDetailModal.test.tsx` — Jest tests for the leaflet modal's SharePoint link rendering

**Files modified:**
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs` — add `SourcePath` to colocated `GetChunkDetailResponse`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs` — assign `SourcePath` in response init
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailRequest.cs` — add `SourcePath` to colocated `GetLeafletChunkDetailResponse`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs` — assign `SourcePath` in response init
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs` — add 3 tests for `SourcePath`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletChunkDetailHandlerTests.cs` — add 3 tests for `SourcePath`
- `frontend/src/api/hooks/useKnowledgeBase.ts` — add `sourcePath?: string` to `GetChunkDetailResponse` interface
- `frontend/src/api/hooks/useLeaflet.ts` — add `sourcePath?: string` to `GetLeafletChunkDetailResponse` interface
- `frontend/src/components/knowledge-base/ChunkDetailModal.tsx` — render conditional SharePoint link
- `frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx` — add 3 tests for SharePoint link
- `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx` — render conditional SharePoint link
- `frontend/src/components/Layout/Sidebar.tsx` — extend marketing section, remove knowledgebase section, drop unused `Database` import
- `frontend/src/App.tsx` — register `/marketing/feedback` route

**Files NOT modified:** `KnowledgeBaseRepository.cs`, `LeafletRepository.cs`, `IKnowledgeBaseRepository.cs`, `ILeafletRepository.cs`, `KnowledgeBaseFeedbackPage.tsx`, the `/knowledge-base/feedback` route entry, the auto-generated `api/generated/api-client.ts` (any diff from `npm run build` is committed as-is but not hand-edited).

---

## Task 1: Backend — KB GetChunkDetail returns `SourcePath`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs`

- [ ] **Step 1: Add the three failing tests**

Open `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs`. The file currently uses raw xUnit `Assert.*` (no FluentAssertions). Match that style.

Insert these three `[Fact]` methods immediately after the existing `Handle_ReturnsNotFound_WhenChunkDoesNotExist` test (before the closing brace of the class):

```csharp
    [Fact]
    public async Task Handle_ReturnsSharePointSourcePath_WhenDocumentSourcedFromSharePoint()
    {
        var chunkId = Guid.NewGuid();
        var chunk = MakeChunk(id: chunkId);
        chunk.Document.SourcePath = "https://anelacz.sharepoint.com/sites/x/doc.docx";

        _repository
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = new GetChunkDetailHandler(_repository.Object);
        var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

        Assert.True(result.Success);
        Assert.Equal("https://anelacz.sharepoint.com/sites/x/doc.docx", result.SourcePath);
    }

    [Fact]
    public async Task Handle_ReturnsSyntheticUploadPath_WhenDocumentManuallyUploaded()
    {
        var chunkId = Guid.NewGuid();
        var chunk = MakeChunk(id: chunkId);
        chunk.Document.SourcePath = "upload/abc-123/file.pdf";

        _repository
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = new GetChunkDetailHandler(_repository.Object);
        var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

        Assert.True(result.Success);
        Assert.Equal("upload/abc-123/file.pdf", result.SourcePath);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyString_WhenDocumentHasNoSourcePath()
    {
        var chunkId = Guid.NewGuid();
        var chunk = MakeChunk(id: chunkId);
        chunk.Document.SourcePath = string.Empty;

        _repository
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = new GetChunkDetailHandler(_repository.Object);
        var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.SourcePath);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetChunkDetailHandlerTests"
```

Expected: build error `'GetChunkDetailResponse' does not contain a definition for 'SourcePath'`. (If the build succeeds — which it should not — the assertions would fail.)

- [ ] **Step 3: Add `SourcePath` to the response DTO**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`, add one property to the `GetChunkDetailResponse` class (after the existing `Content` property, before the constructors):

```csharp
    public string? SourcePath { get; set; }
```

Final shape of the class body (for unambiguous reference):

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
    public string? SourcePath { get; set; }

    public GetChunkDetailResponse() { }

    public GetChunkDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 4: Populate `SourcePath` in the handler**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`, add `SourcePath = chunk.Document.SourcePath,` to the response object initialiser (after `Content = chunk.Content,`).

Final shape of the response init:

```csharp
        return new GetChunkDetailResponse
        {
            ChunkId = chunk.Id,
            DocumentId = chunk.DocumentId,
            Filename = chunk.Document.Filename,
            DocumentType = chunk.DocumentType,
            IndexedAt = chunk.Document.IndexedAt,
            ChunkIndex = chunk.ChunkIndex,
            Summary = chunk.Summary,
            Content = chunk.Content,
            SourcePath = chunk.Document.SourcePath,
        };
```

- [ ] **Step 5: Run tests to verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetChunkDetailHandlerTests"
```

Expected: 5 tests pass (2 existing + 3 new). Zero failures.

- [ ] **Step 6: Build + format the backend**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: clean build, `dotnet format` exits 0 with no changes.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs
git commit -m "feat: surface SourcePath in KB GetChunkDetail response"
```

---

## Task 2: Backend — Leaflet GetLeafletChunkDetail returns `SourcePath`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletChunkDetailHandlerTests.cs`

- [ ] **Step 1: Add the three failing tests**

`GetLeafletChunkDetailHandlerTests.cs` currently uses **FluentAssertions**. Match that style. Insert the three `[Fact]` methods at the end of the class, before the closing brace:

```csharp
    [Fact]
    public async Task Handle_returns_sharepoint_source_path_when_document_sourced_from_sharepoint()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "c",
            Summary = "s",
            Document = new LeafletDocument
            {
                Id = Guid.NewGuid(),
                Filename = "leaflet.pdf",
                SourcePath = "https://anelacz.sharepoint.com/sites/x/leaflet.pdf",
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletChunkDetailRequest { ChunkId = chunkId }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.SourcePath.Should().Be("https://anelacz.sharepoint.com/sites/x/leaflet.pdf");
    }

    [Fact]
    public async Task Handle_returns_synthetic_upload_path_when_document_manually_uploaded()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "c",
            Summary = "s",
            Document = new LeafletDocument
            {
                Id = Guid.NewGuid(),
                Filename = "uploaded.pdf",
                SourcePath = "upload/xyz-9/uploaded.pdf",
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletChunkDetailRequest { ChunkId = chunkId }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.SourcePath.Should().Be("upload/xyz-9/uploaded.pdf");
    }

    [Fact]
    public async Task Handle_returns_empty_string_when_document_has_no_source_path()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "c",
            Summary = "s",
            Document = new LeafletDocument
            {
                Id = Guid.NewGuid(),
                Filename = "no-source.pdf",
                SourcePath = string.Empty,
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletChunkDetailRequest { ChunkId = chunkId }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.SourcePath.Should().Be(string.Empty);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetLeafletChunkDetailHandlerTests"
```

Expected: compilation error — `GetLeafletChunkDetailResponse` does not contain a definition for `SourcePath`.

- [ ] **Step 3: Add `SourcePath` to the response DTO**

In `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailRequest.cs`, add the property to `GetLeafletChunkDetailResponse` (after `Summary`, before the constructors):

```csharp
    public string? SourcePath { get; set; }
```

Final class shape:

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
    public string? SourcePath { get; set; }

    public GetLeafletChunkDetailResponse() { }

    public GetLeafletChunkDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 4: Populate `SourcePath` in the handler**

In `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs`, add `SourcePath = chunk.Document.SourcePath,` to the response object initialiser:

```csharp
        return new GetLeafletChunkDetailResponse
        {
            ChunkId = chunk.Id,
            DocumentId = chunk.DocumentId,
            Filename = chunk.Document.Filename,
            IndexedAt = chunk.Document.IndexedAt,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Summary = chunk.Summary,
            SourcePath = chunk.Document.SourcePath,
        };
```

- [ ] **Step 5: Run tests to verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetLeafletChunkDetailHandlerTests"
```

Expected: 5 tests pass (2 existing + 3 new).

- [ ] **Step 6: Build + format the backend**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: clean build, format exits 0 with no changes.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletChunkDetailHandlerTests.cs
git commit -m "feat: surface SourcePath in Leaflet GetLeafletChunkDetail response"
```

---

## Task 3: Frontend — `getSharePointLink` helper + tests

**Files:**
- Create: `frontend/src/utils/sharepointLink.ts`
- Test: `frontend/src/utils/sharepointLink.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/utils/sharepointLink.test.ts` with the following content:

```ts
import { getSharePointLink } from './sharepointLink';

describe('getSharePointLink', () => {
  test('returns null for null', () => {
    expect(getSharePointLink(null)).toBeNull();
  });

  test('returns null for undefined', () => {
    expect(getSharePointLink(undefined)).toBeNull();
  });

  test('returns null for empty string', () => {
    expect(getSharePointLink('')).toBeNull();
  });

  test('returns null for synthetic upload path', () => {
    expect(getSharePointLink('upload/abc-123/file.pdf')).toBeNull();
  });

  test('returns the URL verbatim when it starts with https://', () => {
    const url = 'https://anelacz.sharepoint.com/sites/x/doc.docx';
    expect(getSharePointLink(url)).toBe(url);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/utils/sharepointLink.test.ts
```

Expected: test fails with "Cannot find module './sharepointLink'".

- [ ] **Step 3: Implement the helper**

Create `frontend/src/utils/sharepointLink.ts`:

```ts
export function getSharePointLink(sourcePath: string | null | undefined): string | null {
  if (!sourcePath) return null;
  if (sourcePath.startsWith('https://')) return sourcePath;
  return null;
}
```

- [ ] **Step 4: Run tests to verify all pass**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/utils/sharepointLink.test.ts
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/utils/sharepointLink.ts frontend/src/utils/sharepointLink.test.ts
git commit -m "feat: add getSharePointLink helper for chunk source URLs"
```

---

## Task 4: Frontend — extend hand-defined TS interfaces

The chunk detail hooks in `api/hooks/` use **hand-maintained** response interfaces, not the auto-generated client. Add the new optional field to both manual interfaces so subsequent UI work compiles.

**Files:**
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`
- Modify: `frontend/src/api/hooks/useLeaflet.ts`

- [ ] **Step 1: Add `sourcePath` to `GetChunkDetailResponse` (KB hook)**

Open `frontend/src/api/hooks/useKnowledgeBase.ts`. Locate the interface at line ~127 and add `sourcePath?: string;` as the last field.

Final shape:

```ts
export interface GetChunkDetailResponse {
  success: boolean;
  chunkId: string;
  documentId: string;
  filename: string;
  documentType: 'KnowledgeBase' | 'Conversation';
  indexedAt: string | null;
  chunkIndex: number;
  summary: string;
  content: string;
  sourcePath?: string;
}
```

- [ ] **Step 2: Add `sourcePath` to `GetLeafletChunkDetailResponse` (Leaflet hook)**

Open `frontend/src/api/hooks/useLeaflet.ts`. Locate the interface at line ~43 and add `sourcePath?: string;` as the last field.

Final shape:

```ts
export interface GetLeafletChunkDetailResponse {
  success: boolean;
  chunkId: string;
  documentId: string;
  filename: string;
  documentType: string;
  indexedAt: string | null;
  chunkIndex: number;
  summary: string;
  content: string;
  sourcePath?: string;
}
```

- [ ] **Step 3: Verify compilation**

```bash
cd frontend && npm run build
```

Expected: build succeeds. (No new code consumes `sourcePath` yet — that arrives in Tasks 5–6.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useKnowledgeBase.ts frontend/src/api/hooks/useLeaflet.ts
git commit -m "feat: add sourcePath to chunk detail response interfaces"
```

---

## Task 5: Frontend — KB ChunkDetailModal renders SharePoint link

**Files:**
- Modify: `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`
- Test: `frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx`

- [ ] **Step 1: Add the three failing test cases**

Append three new tests to `frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx` (after the existing `'calls onClose on Escape key'` test). The file already mocks `useChunkDetailQuery`; reuse the same mock pattern.

```ts
test('renders SharePoint link when sourcePath is an https URL', () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: 'https://anelacz.sharepoint.com/sites/x/doc.docx' },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  const link = screen.getByRole('link', { name: /Otevřít v SharePoint/i });
  expect(link).toHaveAttribute('href', 'https://anelacz.sharepoint.com/sites/x/doc.docx');
  expect(link).toHaveAttribute('target', '_blank');
  expect(link).toHaveAttribute('rel', 'noopener noreferrer');
});

test('hides SharePoint link when sourcePath is a synthetic upload path', () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: 'upload/abc/file.pdf' },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  expect(screen.queryByRole('link', { name: /Otevřít v SharePoint/i })).not.toBeInTheDocument();
});

test('hides SharePoint link when sourcePath is missing', () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: undefined },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  expect(screen.queryByRole('link', { name: /Otevřít v SharePoint/i })).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx
```

Expected: the three new tests fail (link is not in the DOM yet); the existing four tests still pass.

- [ ] **Step 3: Add the link element + imports**

Edit `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`:

3a. Update the `lucide-react` import to include `ExternalLink`:

```ts
import { X, ExternalLink } from 'lucide-react';
```

3b. Add the helper import below the existing `formatDateTime` import:

```ts
import { getSharePointLink } from '../../utils/sharepointLink';
```

3c. Insert the link block immediately after the closing tag of the `Meta row` `<div>` (currently line 74) and before the `Summary` `<div>` (currently line 77). The insertion goes inside the `data && (...)` fragment.

```tsx
              {/* SharePoint link (only when document has a real https:// SourcePath) */}
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

- [ ] **Step 4: Run tests to verify all pass**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx
```

Expected: all 7 tests pass (4 existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/knowledge-base/ChunkDetailModal.tsx \
        frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx
git commit -m "feat: render SharePoint link in KB chunk detail modal"
```

---

## Task 6: Frontend — Leaflet LeafletChunkDetailModal renders SharePoint link

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx`
- Create: `frontend/src/features/leaflet-generator/__tests__/LeafletChunkDetailModal.test.tsx`

There is no existing test file for `LeafletChunkDetailModal`; create one mirroring the KB modal's test structure.

- [ ] **Step 1: Create the failing test file**

Create `frontend/src/features/leaflet-generator/__tests__/LeafletChunkDetailModal.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import LeafletChunkDetailModal from '../LeafletChunkDetailModal';
import * as hooks from '../../../api/hooks/useLeaflet';

const mockChunkDetail = {
  success: true,
  chunkId: 'chunk-1',
  documentId: 'doc-1',
  filename: 'leaflet.pdf',
  documentType: 'Document',
  indexedAt: '2024-03-15T10:00:00Z',
  chunkIndex: 0,
  summary: 'Summary text.',
  content: 'Full leaflet content.',
};

const mockOnClose = jest.fn();

function renderModal() {
  return render(
    <LeafletChunkDetailModal chunkId="chunk-1" onClose={mockOnClose} />
  );
}

beforeEach(() => {
  jest.clearAllMocks();
});

test('renders SharePoint link when sourcePath is an https URL', () => {
  jest.spyOn(hooks, 'useLeafletChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: 'https://anelacz.sharepoint.com/sites/x/leaflet.pdf' },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  const link = screen.getByRole('link', { name: /Otevřít v SharePoint/i });
  expect(link).toHaveAttribute('href', 'https://anelacz.sharepoint.com/sites/x/leaflet.pdf');
  expect(link).toHaveAttribute('target', '_blank');
  expect(link).toHaveAttribute('rel', 'noopener noreferrer');
});

test('hides SharePoint link when sourcePath is a synthetic upload path', () => {
  jest.spyOn(hooks, 'useLeafletChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: 'upload/abc/file.pdf' },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  expect(screen.queryByRole('link', { name: /Otevřít v SharePoint/i })).not.toBeInTheDocument();
});

test('hides SharePoint link when sourcePath is missing', () => {
  jest.spyOn(hooks, 'useLeafletChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: undefined },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  expect(screen.queryByRole('link', { name: /Otevřít v SharePoint/i })).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/leaflet-generator/__tests__/LeafletChunkDetailModal.test.tsx
```

Expected: all three tests fail.

- [ ] **Step 3: Add the link element + imports**

Edit `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx`:

3a. Update the `lucide-react` import:

```ts
import { X, ExternalLink } from 'lucide-react';
```

3b. Add the helper import below the existing `formatDateTime` import:

```ts
import { getSharePointLink } from '../../utils/sharepointLink';
```

3c. Insert the link block immediately after the closing tag of the `Meta row` `<div>` (currently line 66) and before the `Summary` `<div>` (currently line 69):

```tsx
              {/* SharePoint link (only when document has a real https:// SourcePath) */}
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

- [ ] **Step 4: Run tests to verify all pass**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/leaflet-generator/__tests__/LeafletChunkDetailModal.test.tsx
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx \
        frontend/src/features/leaflet-generator/__tests__/LeafletChunkDetailModal.test.tsx
git commit -m "feat: render SharePoint link in Leaflet chunk detail modal"
```

---

## Task 7: Frontend — Marketing Feedback stub page + route registration

**Files:**
- Create: `frontend/src/pages/MarketingFeedbackPage.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Create the stub page**

Create `frontend/src/pages/MarketingFeedbackPage.tsx`:

```tsx
import React from 'react';

const MarketingFeedbackPage: React.FC = () => {
  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      <p className="mt-2 text-sm text-gray-600">
        Přehled zpětné vazby bude dostupný po dokončení integrace.
      </p>
    </div>
  );
};

export default MarketingFeedbackPage;
```

- [ ] **Step 2: Import and register the route in `App.tsx`**

Open `frontend/src/App.tsx`.

2a. Add the import after the existing `KnowledgeBaseFeedbackPage` import (currently line 38):

```ts
import MarketingFeedbackPage from "./pages/MarketingFeedbackPage";
```

2b. Inside the `<Routes>` block, register the new route immediately after the existing `/knowledge-base/feedback` route (currently lines 469–472). Insert before the `/articles` route:

```tsx
                        <Route
                          path="/marketing/feedback"
                          element={<MarketingFeedbackPage />}
                        />
```

- [ ] **Step 3: Verify the build**

```bash
cd frontend && npm run build
```

Expected: build succeeds, no TypeScript errors.

- [ ] **Step 4: Smoke test the route in dev**

This is a manual verification step. Start the dev server in one terminal, then verify the route in a browser.

```bash
cd frontend && npm start
```

Open `http://localhost:3000/marketing/feedback` while authenticated. Expect to see the heading "Feedback" and the subtitle "Přehled zpětné vazby bude dostupný po dokončení integrace." with no console errors. Then stop the dev server (Ctrl+C).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/MarketingFeedbackPage.tsx frontend/src/App.tsx
git commit -m "feat: add /marketing/feedback stub page"
```

---

## Task 8: Frontend — Sidebar consolidation

Merge the existing `marketing` section with the existing `knowledgebase` section into a single `marketing` section containing five items, then remove the now-empty `knowledgebase` section and the `Database` icon import.

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Replace the `marketing` section block**

Open `frontend/src/components/Layout/Sidebar.tsx`. Find the existing `marketing` section (currently lines 140–149):

```tsx
    {
      id: "marketing",
      name: "Marketing",
      icon: Megaphone,
      type: "section" as const,
      items: [
        { id: "marketing-calendar", name: "Kalendář", href: "/marketing/calendar" },
        { id: "leaflet-generator", name: "Generátor letáků", href: "/leaflet-generator" },
      ],
    },
```

Replace it with:

```tsx
    {
      id: "marketing",
      name: "Marketing",
      icon: Megaphone,
      type: "section" as const,
      items: [
        { id: "marketing-calendar", name: "Kalendář", href: "/marketing/calendar" },
        { id: "leaflet-generator", name: "Generátor letáků", href: "/leaflet-generator" },
        { id: "articles", name: "Generátor článků", href: "/articles" },
        { id: "knowledge-base", name: "Poradenství (KB)", href: "/knowledge-base" },
        ...(hasRole("knowledge_base_manager") || hasRole("leaflet_manager") || hasRole("article_generator")
          ? [{ id: "marketing-feedback", name: "Feedback", href: "/marketing/feedback" }]
          : []),
      ],
    },
```

- [ ] **Step 2: Remove the `knowledgebase` section block**

In the same file, delete the entire `knowledgebase` section object (currently lines 299–314):

```tsx
    {
      id: 'knowledgebase',
      name: 'Knowledgebase',
      icon: Database,
      type: 'section' as const,
      items: [
        {
          id: 'kb-poradenstvi',
          name: 'Poradenství',
          href: '/knowledge-base',
        },
        ...(hasRole('knowledge_base_manager')
          ? [{ id: 'kb-feedback', name: 'Feedback', href: '/knowledge-base/feedback' }]
          : []),
      ],
    },
```

After deletion, the immediately preceding entry (the `automatizace` section, ending with `};` and a closing `,`) should be the last array item. The `]` closing `navigationSections` (currently line 315) immediately follows.

- [ ] **Step 3: Remove the `Database` icon import**

`Database` is only used inside the now-removed `knowledgebase` section. Confirm and remove.

3a. Confirm no other usage in the file:

```bash
grep -n "Database" frontend/src/components/Layout/Sidebar.tsx
```

Expected output: zero matches (the previous import line referenced it, but after this step we will have removed the only usage; the import itself is the next match to remove).

3b. In the `lucide-react` import block (currently line 20), remove the line `Database,`. The block should become:

```tsx
import {
  LayoutDashboard,
  Package,
  ShoppingCart,
  ChevronDown,
  ChevronRight,
  PanelLeftClose,
  PanelLeftOpen,
  Menu,
  DollarSign,
  Cog,
  Truck,
  Bot,
  Newspaper,
  Users,
  ExternalLink,
  FileText,
  Megaphone,
} from "lucide-react";
```

- [ ] **Step 4: Verify build + lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: clean build, lint reports no new errors. Specifically, no "unused import" error for `Database`.

- [ ] **Step 5: Smoke test the sidebar in dev**

```bash
cd frontend && npm start
```

Authenticated as a manager (with one of `knowledge_base_manager`, `leaflet_manager`, `article_generator` roles), expand the sidebar's Marketing group and verify these five items appear in this order:

1. Kalendář
2. Generátor letáků
3. Generátor článků
4. Poradenství (KB)
5. Feedback

Click each item and verify it navigates without a console error. Confirm there is no "Knowledgebase" group anywhere. Stop the dev server.

If you can also test as a non-manager (no relevant roles), confirm that "Feedback" is hidden but the other four items remain.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: consolidate Marketing sidebar group"
```

---

## Task 9: End-to-end verification

Final verification before merging the branch. All previous tasks must be committed.

- [ ] **Step 1: Backend full build, format, and tests**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/Anela.Heblo.sln
```

Expected: build clean, format reports no changes, all tests pass.

- [ ] **Step 2: Frontend full build, lint, and tests**

```bash
cd frontend && npm run build && npm run lint && CI=true npx react-scripts test --watchAll=false
```

Expected: build clean, lint clean, all Jest tests pass (including the new `sharepointLink.test.ts`, the extended `ChunkDetailModal.test.tsx`, and the new `LeafletChunkDetailModal.test.tsx`).

- [ ] **Step 3: Manual smoke checklist**

Run the dev server, sign in as a manager.

```bash
cd frontend && npm start
```

Walk through:
1. Sidebar's Marketing group expands and shows Kalendář, Generátor letáků, Generátor článků, Poradenství (KB), Feedback.
2. There is no Knowledgebase group.
3. `/marketing/feedback` renders the stub.
4. `/knowledge-base` → Dokumenty tab → click a chunk row whose document originated in SharePoint → modal shows "Otevřít v SharePoint" link with `target="_blank"` and the correct URL.
5. `/knowledge-base` → Dokumenty tab → click a chunk row for a manually uploaded document → modal shows no SharePoint link.
6. `/leaflet-generator` → Dokumenty tab → repeat steps 4–5 with the Leaflet modal.
7. No browser console errors.

Stop the dev server.

- [ ] **Step 4: Confirm working tree is clean**

```bash
git status
```

Expected: clean working tree (all task commits already in). If the OpenAPI auto-regen produced a diff in `frontend/src/api/generated/api-client.ts` during `npm run build` (the generator may add `sourcePath` to the generated DTO), commit it now:

```bash
git status
git add frontend/src/api/generated/api-client.ts
git diff --cached frontend/src/api/generated/api-client.ts | head -60
git commit -m "chore: regenerate OpenAPI client for sourcePath addition"
```

If there is no diff, skip this commit.

---

## Self-Review Outcomes

- **Spec coverage:**
  - FR-1 (Marketing consolidation) → Task 8.
  - FR-2 (Campaigns disposition) → no-op; spec premise is false. Plan documents this in the introduction; PR description should restate.
  - FR-3 (Marketing Feedback stub) → Task 7.
  - FR-4 (KB SourcePath in DTO + handler) → Task 1.
  - FR-5 (Leaflet SourcePath in DTO + handler) → Task 2.
  - FR-6 (`getSharePointLink` helper) → Task 3 (relocated to `frontend/src/utils/` per arch-review Decision 1).
  - FR-7 (KB modal renders link) → Task 5.
  - FR-8 (Leaflet modal renders link) → Task 6.
  - Hidden gap (manual TS interfaces in `api/hooks/`) → Task 4.

- **Type consistency:** Helper signature matches across all consumers: `getSharePointLink(sourcePath: string | null | undefined): string | null`. Both modals call it identically. The DTO field is `string?` in C# and `sourcePath?: string` in TS. The helper's `if (!sourcePath)` check covers `null`, `undefined`, and `""` uniformly.

- **No placeholders:** Each step contains the exact code or command needed; no "add appropriate handling" or "similar to other modal" references.
