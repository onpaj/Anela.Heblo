# Specification: Extract IndexDocumentHandler.Handle into private phase methods

## Summary
`IndexDocumentHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs:27-136`) is a single 110-line method mixing de-duplication policy, document lifecycle management, and indexing orchestration. This is a pure structural refactor: extract the existing logic into named private async methods so `Handle` reads as a short orchestrator, with no behavioral change to any of the de-duplication, persistence, indexing, error-handling, or response paths.

## Background
The KnowledgeBase module's `IndexDocument` use case receives a document (from a OneDrive sync or a manual upload), resolves its content type, computes a content hash, checks for duplicates via two independent strategies (hash-based and identity-based), and either short-circuits with the existing document or creates, persists, and indexes a new one. The arch-review routine flagged the method as exceeding the project's 50-line soft limit (110 lines) and interleaving six distinct logical phases, making it harder to test, extend, or reason about failure modes in isolation. `docs/architecture/development_guidelines.md` and the review criteria call for single-responsibility methods; this spec covers extracting the phases without altering behavior.

## Functional Requirements

### FR-1: Extract duplicate-resolution phase
Extract hash-based and identity-based duplicate detection (current lines 36-84) into a private method, e.g. `TryResolveDuplicateAsync(IndexDocumentRequest request, string contentType, string contentHash, CancellationToken cancellationToken)`, returning `Task<IndexDocumentResponse?>` — non-null when a duplicate was resolved and the caller should return immediately, null when the caller should proceed to create a new document.

This method must preserve, unchanged:
- Hash-based duplicate lookup via `_repository.GetDocumentByHashAsync`.
  - If found and `SourcePath` differs from the request's `SourcePath`, call `_repository.UpdateDocumentSourcePathAsync` and log the path update (info level).
  - If found and `SourcePath` matches, log at debug level that the document is being skipped (hash match) — no repository call.
  - In both hash-match sub-cases, if `useGraphIdentity` is true (both `GraphItemId` and `DriveId` are non-empty on the request) and the existing document's `GraphItemId` is null, call `_repository.UpdateDocumentGraphItemIdAsync` to backfill and log at info level. If the existing document already has a `GraphItemId`, do not call it.
  - When a hash match is found (either sub-case), return an `IndexDocumentResponse` built from the *existing* document with `WasDuplicate = true` and the exact same field mapping as today (`DocumentId`, `Status`, `Filename`, `ContentType`, `CreatedAt`, `IndexedAt`).
- Identity-based duplicate lookup, only when no hash match was found:
  - When `useGraphIdentity` is true, look up via `_repository.GetDocumentByGraphItemIdAsync(request.DriveId!, request.GraphItemId!, cancellationToken)`.
  - Otherwise, look up via `_repository.GetDocumentBySourcePathAsync(request.SourcePath, cancellationToken)`.
  - If found, log at info level and call `_repository.DeleteDocumentAsync` to remove the stale document, then return null (caller proceeds to create a new document — this path is *not* treated as a duplicate response).
  - If not found, return null.

**Acceptance criteria:**
- All 13 existing tests in `IndexDocumentHandlerTests.cs` continue to pass unmodified.
- `useGraphIdentity` computation (`!string.IsNullOrEmpty(request.GraphItemId) && !string.IsNullOrEmpty(request.DriveId)`) remains identical and is computed once, before duplicate resolution, in the same place relative to `contentHash`/`contentType` (either in `Handle` and passed in, or recomputed identically inside the extracted method — implementer's choice, behavior must be identical).
- No additional repository calls are introduced or removed; call order is preserved.

### FR-2: Extract document creation/persist phase
Extract construction and initial persistence of a new `KnowledgeBaseDocument` (current lines 86-101) into a private method, e.g. `CreateAndPersistDocumentAsync(IndexDocumentRequest request, string contentType, string contentHash, CancellationToken cancellationToken)`, returning `Task<KnowledgeBaseDocument>`.

Must preserve, unchanged:
- All field assignments exactly as today, including `Id = Guid.NewGuid()`, `Status = DocumentStatus.Processing`, `CreatedAt = DateTime.UtcNow`, and `DriveId`/`GraphItemId` set only when `useGraphIdentity` is true (null otherwise).
- The call sequence `_repository.AddDocumentAsync(document, cancellationToken)` followed by `_repository.SaveChangesAsync(cancellationToken)`.

**Acceptance criteria:**
- `Handle_StoresDocumentAndChunksWithEmbeddings` and `Handle_SetsDocumentTypeFromRequest` continue to pass, confirming field-for-field equivalence.
- `SaveChangesAsync` is still called exactly once here (the test suite verifies a total of 2 calls across create+index in the happy path).

### FR-3: Extract indexing-with-error-handling phase
Extract the indexing call and its nested try/catch (current lines 103-122) into a private method, e.g. `IndexWithErrorHandlingAsync(KnowledgeBaseDocument document, IndexDocumentRequest request, CancellationToken cancellationToken)`, returning `Task`.

Must preserve, unchanged:
- The call to `_indexingService.IndexChunksAsync(request.Content, contentType, document, cancellationToken)` followed by `_repository.SaveChangesAsync(cancellationToken)` on the success path. (Note: `contentType` must be available to this method — either passed in or read from `document.ContentType`, which is set identically; implementer's choice as long as the value passed to `IndexChunksAsync` is unchanged.)
- On exception: log at error level, set `document.Status = DocumentStatus.Failed`, attempt `_repository.SaveChangesAsync(cancellationToken)` in a nested try/catch that logs (error level) and swallows any exception from *that* save, then re-throw the original exception (`throw;`, preserving the original stack trace).
- The final info-level "Indexed document {Filename}" log line, which happens only on the success path, must remain associated with successful completion of this phase — placed either at the end of this method or immediately after it returns in `Handle`, as long as it does not execute on the error path (it does not today, since the method returns via `throw`).

**Acceptance criteria:**
- `Handle_IndexChunksThrows_SetsStatusToFailedAndRethrows` continues to pass: exception propagates, `document.Status == DocumentStatus.Failed`, `SaveChangesAsync` called at least twice.
- `Handle_ThrowsForUnsupportedContentType` and `Handle_ThrowsNotSupportedException_WhenNoExtractorMatchesContentType` continue to pass.

### FR-4: Extract response-building phase
Extract the final `IndexDocumentResponse` construction (current lines 126-135, for the non-duplicate path) into a private method, e.g. `BuildResponse(KnowledgeBaseDocument document, bool wasDuplicate)`, returning `IndexDocumentResponse`. The duplicate-path response construction (inside FR-1's extracted method) may reuse this same helper if convenient, or remain inline — both are acceptable as long as the resulting `IndexDocumentResponse` field values are unchanged in every case.

**Acceptance criteria:**
- Response field mapping (`DocumentId`, `Status`, `WasDuplicate`, `Filename`, `ContentType`, `CreatedAt`, `IndexedAt`) is byte-for-byte identical to today for both the duplicate and non-duplicate paths.

### FR-5: `Handle` becomes a thin orchestrator
`Handle` retains: the initial info-level "Indexing document..." log, content-type resolution (`ContentTypeResolver.Resolve`), content-hash computation (`SHA256.HashData` + `Convert.ToHexString`), and calls to the four extracted methods in the same order as today, returning early when duplicate resolution yields a non-null response.

**Acceptance criteria:**
- `Handle`'s body is short enough to read as a linear orchestration (target: well under the 50-line soft limit), with no duplicated business logic between `Handle` and the extracted methods.
- Method signature `Task<IndexDocumentResponse> Handle(IndexDocumentRequest request, CancellationToken cancellationToken)` is unchanged (interface contract, `IRequestHandler<IndexDocumentRequest, IndexDocumentResponse>`).

## Non-Functional Requirements

### NFR-1: No behavior change
This is a structural-only refactor. Every observable behavior — return values, exceptions thrown, repository/service calls made (including arguments and call counts), and log messages/levels — must be identical before and after. The existing test suite (`backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`, 13 tests) is the behavioral contract and must pass unmodified — no test file changes are in scope for this refactor.

### NFR-2: Method length
Each extracted private method should itself stay reasonably short and single-purpose; none should reintroduce a 100+ line block. `Handle` itself should end up well under the project's 50-line soft limit.

## Data Model
No data model changes. `KnowledgeBaseDocument`, `IndexDocumentRequest`, `IndexDocumentResponse`, and `DocumentStatus` are unchanged.

## API / Interface Design
No public API changes. `IndexDocumentHandler` remains `public class IndexDocumentHandler : IRequestHandler<IndexDocumentRequest, IndexDocumentResponse>` with the same constructor signature (`IKnowledgeBaseRepository repository, IDocumentIndexingService indexingService, ILogger<IndexDocumentHandler> logger`) and the same public `Handle` method signature. All new methods are `private`.

## Dependencies
None beyond what `IndexDocumentHandler` already depends on: `IKnowledgeBaseRepository`, `IDocumentIndexingService`, `ILogger<IndexDocumentHandler>`, `ContentTypeResolver`.

## Out of Scope
- Any change to de-duplication policy, indexing strategy, or error-handling behavior.
- Any change to `IndexDocumentRequest`, `IndexDocumentResponse`, `KnowledgeBaseDocument`, or repository/service interfaces.
- Adding new tests (existing tests already cover every branch this refactor touches; the acceptance bar is that they pass unmodified).
- Renaming or restructuring anything outside `IndexDocumentHandler.cs`.

## Open Questions
None.

## Status: COMPLETE
