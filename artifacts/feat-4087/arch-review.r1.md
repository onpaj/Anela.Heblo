# Architecture Review: Extract IndexDocumentHandler.Handle into private phase methods

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal refactor of a single MediatR handler's private control flow — no new endpoint, contract, DTO, or persistence shape. It fits the codebase's existing convention cleanly: extracting `private async Task`/`private async Task<T>` helper methods inside a handler class is an established pattern already used in several other handlers in this Application layer (e.g. `ScanPackingOrderHandler.TryMarkAsPackedAsync`/`ResolvePackerAsync`/`BackfillExistingShipmentPackagesAsync`, `SubmitManufactureHandler`, `GetManufactureProtocolHandler`). No new module boundary, no cross-module contract, no architectural decision is required beyond following that existing style. The refactor is confined entirely to `IndexDocumentHandler.cs`; its constructor, public `Handle` signature, and its `IRequestHandler<IndexDocumentRequest, IndexDocumentResponse>` contract are unchanged, so nothing outside the file (controllers, DI registration, other handlers) is affected.

## Proposed Architecture

### Component Overview
```
IndexDocumentHandler (unchanged public surface)
 └─ Handle(request, ct)                              [orchestrator, ~15-20 lines]
     ├─ ContentTypeResolver.Resolve(...)              (unchanged, inline)
     ├─ SHA256 hash computation                        (unchanged, inline)
     ├─ TryResolveDuplicateAsync(...)  ─────────────►  returns IndexDocumentResponse? 
     │      ├─ hash-match branch (update path / backfill GraphItemId)
     │      └─ identity-match branch (delete stale doc)
     │            [early return to Handle when non-null]
     ├─ CreateAndPersistDocumentAsync(...) ─────────►  returns KnowledgeBaseDocument
     ├─ IndexWithErrorHandlingAsync(document, ...) ──►  try/catch, sets Failed + rethrows
     └─ BuildResponse(document, wasDuplicate: false) ─► returns IndexDocumentResponse
```
No new classes, interfaces, or files. All four extracted methods are `private` on `IndexDocumentHandler`, using its existing injected fields (`_repository`, `_indexingService`, `_logger`) directly — no parameter object needed given the field count.

### Key Design Decisions

#### Decision 1: Where does `useGraphIdentity` get computed?
**Options considered:**
(a) Compute once in `Handle`, pass as a `bool` parameter into `TryResolveDuplicateAsync` and `CreateAndPersistDocumentAsync`.
(b) Recompute independently inside each extracted method that needs it.

**Chosen approach:** (a) — compute once in `Handle` (`var useGraphIdentity = !string.IsNullOrEmpty(request.GraphItemId) && !string.IsNullOrEmpty(request.DriveId);`), pass it as a parameter to `TryResolveDuplicateAsync` and `CreateAndPersistDocumentAsync`.

**Rationale:** It is a pure function of `request`, computed once today; keeping it single-sourced in `Handle` avoids two independent copies of the same boolean expression drifting apart under future edits, and costs nothing (it's a trivial parameter). This is a style preference, not a behavioral requirement — the spec (FR-1) explicitly allows either.

#### Decision 2: Where does the final success log line live?
**Options considered:**
(a) Keep `"Indexed document {Filename}"` (info) inside `IndexWithErrorHandlingAsync`, at the very end (only reached on the non-throwing path).
(b) Move it back into `Handle`, immediately after the call to `IndexWithErrorHandlingAsync` returns.

**Chosen approach:** (b) — log it in `Handle`, right after `await IndexWithErrorHandlingAsync(...)` returns (which only happens on success, since the method rethrows on failure).

**Rationale:** Keeps `IndexWithErrorHandlingAsync` focused purely on "call the indexer and handle its failure," and keeps the observable narrative of `Handle` ("start → resolve duplicate → create → index → done") readable top-to-bottom in the orchestrator. Either placement is behaviorally identical (the throw path never reaches it either way); this is purely a readability call.

#### Decision 3: Should the duplicate-path response also go through `BuildResponse`?
**Options considered:**
(a) One `BuildResponse(KnowledgeBaseDocument document, bool wasDuplicate)` helper used by both the duplicate path (inside `TryResolveDuplicateAsync`) and the new-document path (in `Handle`).
(b) Two separate, inline response constructions (status quo shape, just relocated).

**Chosen approach:** (a) — single shared `BuildResponse` helper, since the field mapping (`DocumentId`, `Status`, `WasDuplicate`, `Filename`, `ContentType`, `CreatedAt`, `IndexedAt`) is already identical between the two call sites in the current code (only `WasDuplicate` and which document instance differ).

**Rationale:** Removes the one piece of near-duplication the current method has, at zero risk — a straight field-for-field mapping — and is explicitly permitted by spec FR-4 as the preferred (not mandatory) option.

## Implementation Guidance

### Directory / Module Structure
No new files. All changes confined to:
`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`

### Interfaces and Contracts
No public interface or contract changes. Suggested private method signatures (names are suggestions from the issue; keep them or use close equivalents — no external caller depends on these names):

```csharp
private async Task<IndexDocumentResponse?> TryResolveDuplicateAsync(
    IndexDocumentRequest request, string contentType, string contentHash,
    bool useGraphIdentity, CancellationToken cancellationToken)

private async Task<KnowledgeBaseDocument> CreateAndPersistDocumentAsync(
    IndexDocumentRequest request, string contentType, string contentHash,
    bool useGraphIdentity, CancellationToken cancellationToken)

private async Task IndexWithErrorHandlingAsync(
    KnowledgeBaseDocument document, byte[] content, CancellationToken cancellationToken)

private static IndexDocumentResponse BuildResponse(KnowledgeBaseDocument document, bool wasDuplicate)
```

Note `IndexWithErrorHandlingAsync` needs `request.Content` for `_indexingService.IndexChunksAsync(content, contentType, document, cancellationToken)` — pass `request.Content` (or the whole `request`) plus `document`; `contentType` can be read from `document.ContentType` (set identically to the local `contentType` in `CreateAndPersistDocumentAsync`) rather than threaded as a separate parameter, developer's choice.

### Data Flow
Unchanged from today — see spec's Functional Requirements for the exact branch-by-branch behavior. The only change is *where* each branch's code lives, not what it does or in what order repository/service calls happen.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Subtle behavior drift while splitting a 110-line method (e.g. dropping the nested try/catch around the Failed-status save, or reordering the hash-match sub-branches) | Medium | The existing 13-test suite in `IndexDocumentHandlerTests.cs` already covers every branch (hash match same/different path, backfill vs. no-backfill, identity match via GraphItemId vs. SourcePath, success indexing, indexing failure with Failed-status persistence, content-type resolution). Run the full suite unmodified after the refactor; do not touch the test file. |
| `useGraphIdentity` or `contentHash` recomputed inconsistently between extracted methods | Low | Compute each exactly once in `Handle` and pass as parameters (Decision 1) rather than recomputing in multiple places. |
| Nested try/catch around the Failed-status save (lines 112-119 today) accidentally flattened or its swallow-and-continue semantics changed | Medium | Preserve the nested try/catch verbatim inside `IndexWithErrorHandlingAsync`; `Handle_IndexChunksThrows_SetsStatusToFailedAndRethrows` specifically asserts the original exception (not a save-during-catch exception) propagates. |

## Specification Amendments
None — the spec (`spec.r1.md`) is implementation-ready as written; this review's three Key Design Decisions resolve the spec's explicitly-left-open implementer choices (they were already marked "implementer's choice" in FR-1/FR-3/FR-4) rather than amending any requirement.

## Prerequisites
None. No migrations, config, or infrastructure changes — this is a same-file, same-behavior structural refactor that can be implemented and merged independently.
