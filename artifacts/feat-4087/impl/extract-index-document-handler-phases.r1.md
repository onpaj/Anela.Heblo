# Implementation: extract-index-document-handler-phases

## What was implemented

`IndexDocumentHandler.Handle` was refactored from one large method into a thin
orchestrator plus four private helper methods, with no behavior change:

- `TryResolveDuplicateAsync` — hash-based duplicate detection (short-circuit path) followed
  by identity-based duplicate detection (GraphItemId or SourcePath fallback), including the
  stale-document delete when an identity match is found.
- `CreateAndPersistDocumentAsync` — builds the new `KnowledgeBaseDocument` and persists it.
- `IndexWithErrorHandlingAsync` — runs `IDocumentIndexingService.IndexChunksAsync` and the
  nested try/catch that marks the document Failed and rethrows on error.
- `BuildResponse` — shared `IndexDocumentResponse` construction for both the duplicate-by-hash
  path and the newly-indexed-document path.

The replacement code was applied exactly as specified in the task (Step 2), verbatim —
no deviation.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` — extract-method refactor of `Handle` into private phase methods; no public API, DI, or behavior changes.

## Tests

- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs` — read-only, unmodified. Contains 15 `[Fact]` tests (see Notes below re: the task's "13 tests" figure) covering: store+embed happy path, unsupported/unknown content-type propagation, DocumentType passthrough, hash-duplicate (same path / different path, incl. SourcePath update), identity-duplicate-by-path (delete+reindex), IndexChunks failure → Failed status + rethrow, octet-stream content-type resolution via extension, GraphItemId/DriveId identity flows (exists/doesn't-exist/null/partial), and hash-match legacy-doc GraphItemId backfill (with/without existing value).
  - Ran before refactor: **15/15 passed, 0 failed**.
  - Ran after refactor: **15/15 passed, 0 failed** — identical result.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.KnowledgeBase.UseCases.IndexDocumentHandlerTests"
# Passed! - Failed: 0, Passed: 15, Skipped: 0, Total: 15

dotnet build
# Build succeeded. 0 Error(s), 93 Warning(s) — none in IndexDocumentHandler.cs

dotnet test
# Failed! summary present, but every failure is pre-existing/environmental (see Notes)

dotnet format --verify-no-changes
# exits 0, no output — no formatting violations
```

## Notes

- **Test count discrepancy**: the task description states "the existing 13-test suite" /
  "all 13 tests", but `IndexDocumentHandlerTests.cs` actually contains 15 `[Fact]` methods.
  This is a pre-existing inaccuracy in the task spec, not something introduced here — the
  file was read-only and unmodified throughout. The actual contract (all tests in the file
  pass unmodified, before and after) is satisfied: 15/15 both times.
- **`dotnet test` (full suite) failures are pre-existing and environmental**, unrelated to
  this change. Full run: 190 failing tests total, entirely explained by three root causes
  present in this sandbox, none touching `IndexDocumentHandler` or `KnowledgeBase`
  application logic:
  - 107 failures: `System.ArgumentException: Docker is either not running or misconfigured`
    (Testcontainers-based Postgres integration tests, e.g.
    `KnowledgeBaseRepositoryIntegrationTests`, `ArticleRepositoryFeedbackProjectionSqlTests`)
  - 70 failures: missing `FlexiIntegrationTestFixture` fixture data
    (`Anela.Heblo.Adapters.Flexi.Tests`)
  - 13 failures: live Shoptet API / missing secrets (401 Unauthorized, missing
    `Shoptet:StatusId:EXP` config, placeholder URLs) in `Anela.Heblo.Adapters.Shoptet.Tests`
  I grepped the full failure list for any test whose name contains "IndexDocument" or is in
  the `IndexDocumentHandlerTests` class — none appear. The only KnowledgeBase-related
  failures are `KnowledgeBaseRepositoryIntegrationTests` (Docker-dependent repository
  integration tests, unrelated to this handler), all failing on the identical
  Docker-unavailable error.
- No deviation from the exact replacement code given in Step 2 of the task.
- `git status` after the commit shows only `artifacts/feat-4087/state.json` as modified
  (pre-existing pipeline-managed file, dirty before this task started) — not touched or
  committed by this task, per the "surgical changes" / single-file scope instruction.

## PR Summary

Pure extract-method refactor of `IndexDocumentHandler.Handle` (KnowledgeBase IndexDocument
use case) into named private phase methods — duplicate resolution, document creation,
indexing-with-error-handling, and response building — with zero behavior change. Applied
the exact replacement code specified in the task. Verified via the existing (unmodified)
`IndexDocumentHandlerTests` suite passing identically before and after (15/15), a full
solution build with 0 errors and no new warnings, a full `dotnet test` run whose 190
failures are all pre-existing/environmental (Docker unavailable, missing Flexi fixture,
live Shoptet API/secrets) and unrelated to this file, and a clean `dotnet format
--verify-no-changes`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` — split `Handle` into `TryResolveDuplicateAsync`, `CreateAndPersistDocumentAsync`, `IndexWithErrorHandlingAsync`, and `BuildResponse`.

## Status
DONE
