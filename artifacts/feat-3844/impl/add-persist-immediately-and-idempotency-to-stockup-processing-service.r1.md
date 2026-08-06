# Implementation: add-persist-immediately-and-idempotency-to-stockup-processing-service

## What was implemented
Added a `bool persistImmediately = true` parameter (placed after `CancellationToken`) to
`IStockUpProcessingService.CreateOperationAsync` / `StockUpProcessingService.CreateOperationAsync`,
and added an idempotency pre-check via the existing `IStockUpOperationRepository.GetByDocumentNumberAsync`
so that creating an operation whose `DocumentNumber` already exists is a silent, logged no-op instead
of an unhandled unique-constraint violation. When `persistImmediately` is `false`, the new
`StockUpOperation` is staged via `AddAsync` but `SaveChangesAsync` is not called — the caller becomes
responsible for a later `SaveChangesAsync` that flushes it together with other pending changes on the
same `ApplicationDbContext`, as one atomic commit.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs` — added `persistImmediately` parameter + updated XML doc.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs` — added the `GetByDocumentNumberAsync` pre-check (return early, log, skip) and made `SaveChangesAsync` conditional on `persistImmediately`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs` — added 3 new tests: `CreateOperationAsync_DocumentNumberAlreadyExists_SkipsCreateAndDoesNotSave`, `CreateOperationAsync_DocumentNumberDoesNotExist_PersistImmediatelyDefaultTrue_AddsAndSaves`, `CreateOperationAsync_PersistImmediatelyFalse_AddsButDoesNotSave`.

## Tests
`StockUpProcessingServiceTests` — 6 tests total (3 pre-existing `ProcessPendingOperations_*` tests, unchanged and still passing, + 3 new `CreateOperationAsync_*` tests covering: idempotent skip, default-persist-immediately-true add+save, and persist-immediately-false add-without-save).

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpProcessingServiceTests"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```
All 6 tests pass; build succeeds with 0 errors; format reports no changes.

## Notes
No deviations from the task-context file — implemented exactly as specified, including the parameter
placement rationale (after `CancellationToken`, not before, so existing callers that pass `ct`
positionally as their last argument are unaffected and keep getting `persistImmediately: true`).

## PR Summary
This is task 1 of 3 implementing the fix for GitHub issue #3844 (TransportBox Receive commits
StockUpOperation rows in a separate, non-atomic transaction from the box state change). This task
lays the groundwork in the shared `StockUpProcessingService`: it adds the `persistImmediately` toggle
and the idempotency pre-check that later tasks build on. No behavior change yet for any existing
caller — `persistImmediately` defaults to `true`, preserving today's immediate-commit behavior.

### Changes
- `IStockUpProcessingService.cs` / `StockUpProcessingService.cs` — new optional parameter + idempotency pre-check.
- `StockUpProcessingServiceTests.cs` — 3 new tests.

## Status
DONE
