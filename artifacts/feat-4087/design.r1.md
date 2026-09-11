# Design: Extract IndexDocumentHandler.Handle into private phase methods

## Component Design

Single class, no new components. `IndexDocumentHandler` (`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`) keeps its existing public surface — constructor `(IKnowledgeBaseRepository repository, IDocumentIndexingService indexingService, ILogger<IndexDocumentHandler> logger)` and `Task<IndexDocumentResponse> Handle(IndexDocumentRequest, CancellationToken)` — and gains four `private` methods that partition its current 110-line body:

- **`Handle`** (orchestrator) — logs the start event, resolves content type (`ContentTypeResolver.Resolve`), computes the content hash (`SHA256`/`Convert.ToHexString`), computes `useGraphIdentity`, then delegates to the four methods below in sequence, returning early when duplicate resolution produces a response.

- **`TryResolveDuplicateAsync(request, contentType, contentHash, useGraphIdentity, cancellationToken) -> IndexDocumentResponse?`** — owns both duplicate-detection strategies:
  - Hash-based: `_repository.GetDocumentByHashAsync`; on match, updates source path if moved, backfills `GraphItemId`/`DriveId` if legacy + `useGraphIdentity`, and returns a `WasDuplicate = true` response built from the existing document.
  - Identity-based (only reached when no hash match): `_repository.GetDocumentByGraphItemIdAsync` (when `useGraphIdentity`) or `_repository.GetDocumentBySourcePathAsync` (fallback); on match, deletes the stale document via `_repository.DeleteDocumentAsync` and returns `null` (caller proceeds to create a fresh document — this is a "replace," not treated as duplicate).
  - Returns `null` when neither strategy finds a match.

- **`CreateAndPersistDocumentAsync(request, contentType, contentHash, useGraphIdentity, cancellationToken) -> KnowledgeBaseDocument`** — builds a new `KnowledgeBaseDocument` (status `Processing`, `Id = Guid.NewGuid()`, `CreatedAt = DateTime.UtcNow`, `DriveId`/`GraphItemId` set only when `useGraphIdentity`), then `_repository.AddDocumentAsync` + `_repository.SaveChangesAsync`.

- **`IndexWithErrorHandlingAsync(document, content, cancellationToken) -> Task`** — calls `_indexingService.IndexChunksAsync(content, document.ContentType, document, cancellationToken)` then `_repository.SaveChangesAsync`; on exception, logs, sets `document.Status = DocumentStatus.Failed`, attempts a nested `SaveChangesAsync` (itself wrapped in try/catch that logs and swallows only *that* save's failure), then rethrows the original exception.

- **`BuildResponse(document, wasDuplicate) -> IndexDocumentResponse`** — maps `DocumentId`, `Status`, `WasDuplicate`, `Filename`, `ContentType`, `CreatedAt`, `IndexedAt` from a `KnowledgeBaseDocument`. Shared by both the duplicate path (inside `TryResolveDuplicateAsync`) and the newly-created-document path (in `Handle`, after `IndexWithErrorHandlingAsync` returns).

No new interfaces, no new DI registrations, no change to how `IndexDocumentHandler` is resolved by MediatR.

## Data Schemas

No schema changes. `KnowledgeBaseDocument`, `IndexDocumentRequest`, `IndexDocumentResponse`, and `DocumentStatus` are unchanged — see `spec.r1.md` (Data Model, API / Interface Design) for the unchanged shapes. This refactor touches only the internal control flow of one handler method; no request/response contract, database column, or event payload is affected.
