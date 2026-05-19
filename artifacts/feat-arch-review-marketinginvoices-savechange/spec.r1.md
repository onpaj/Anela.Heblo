# Specification: Batch SaveChangesAsync in MarketingInvoiceImportService

## Summary
`MarketingInvoiceImportService.ImportAsync` currently calls `SaveChangesAsync` once per imported transaction inside its loop, producing one database round-trip per record. This change moves the flush outside the loop so a single `SaveChangesAsync` persists the whole batch, cutting unnecessary I/O on a job that runs twice daily and making the import behave as an all-or-nothing unit of work.

## Background
The MarketingInvoices module imports marketing-spend transactions (e.g. ad platform charges) into the `ImportedMarketingTransaction` table for later sync. A scheduled job invokes `ImportAsync(from, to)` with a 7-day lookback window, twice per day.

Inside the per-transaction loop, the service does:

```csharp
await _repository.AddAsync(entity, ct);
await _repository.SaveChangesAsync(ct);   // one INSERT + COMMIT per transaction
```

`AddAsync` already stages the entity in EF Core change tracking. Calling `SaveChangesAsync` after every `AddAsync` issues N separate `INSERT + COMMIT` round-trips for N new transactions instead of one. `SaveChangesAsync` on `IImportedMarketingTransactionRepository` is a unit-of-work flush; the intended usage is one call per logical batch.

Beyond the I/O cost, per-record commit leaves the table in a partially-imported state if the process is interrupted mid-loop, with no marker distinguishing records from the current run. A single batched flush gives clean all-or-nothing semantics for the import.

This was filed by the daily arch-review routine on 2026-05-18. It is a targeted refactor: behavior visible to callers (the `MarketingImportResult` counts) must remain correct.

## Functional Requirements

### FR-1: Single batched flush per import run
`SaveChangesAsync` must be called at most once per `ImportAsync` invocation, after the transaction loop completes, instead of once per imported record.

**Acceptance criteria:**
- The `await _repository.SaveChangesAsync(ct)` call is removed from inside the `foreach` loop.
- After the loop, `SaveChangesAsync(ct)` is called exactly once when at least one entity was staged via `AddAsync` (i.e. `result.Imported > 0` before the final save).
- When no new entities were staged (all transactions skipped/failed before `AddAsync`, or the source returned an empty list), `SaveChangesAsync` is not called.
- `ExistsAsync`, `AddAsync`, duplicate-skip logic, and per-transaction `try/catch` remain inside the loop and behave as today.

### FR-2: Preserve `MarketingImportResult` count semantics
The `Imported`, `Skipped`, and `Failed` counts returned to callers must continue to reflect the true outcome of the run.

**Detailed description:** `result.Imported` is incremented inside the loop after a successful `AddAsync` (records staged in change tracking). `result.Skipped` increments for duplicates detected by `ExistsAsync`. `result.Failed` increments when a per-transaction operation throws. The final batched `SaveChangesAsync` must not silently leave `Imported` reporting records that were never persisted.

**Acceptance criteria:**
- For an import where all transactions are new and the flush succeeds, `Imported` equals the number of new transactions, `Skipped == 0`, `Failed == 0`.
- For a mix of new and duplicate transactions, `Imported` counts only new records and `Skipped` counts duplicates.
- A per-transaction exception (e.g. inside `ExistsAsync` or `AddAsync`) increments `Failed`, does not abort the run, and the remaining transactions are still processed.

### FR-3: Handle failure of the final batched flush
If the single post-loop `SaveChangesAsync` throws, the batch was not persisted; the result must not report those records as imported.

**Detailed description:** With per-record save, a failure affected only one record. With a batched save, a flush failure means none of the staged records were committed. Assumption (see Open Questions — resolved): the post-loop `SaveChangesAsync` is wrapped in a `try/catch`. On exception the error is logged with structured context, and the staged count is moved from `Imported` to `Failed` so the returned `MarketingImportResult` reflects that nothing was persisted.

**Acceptance criteria:**
- The post-loop `SaveChangesAsync` is wrapped in `try/catch`.
- On a flush exception: the exception is logged via `_logger.LogError` with the platform and the affected record count; `result.Failed` is increased by the previously-staged `Imported` count and `result.Imported` is set to `0`.
- The exception is not rethrown; `ImportAsync` returns the corrected `MarketingImportResult` normally (consistent with the existing per-transaction error-isolation style).
- The final completion log line reports the corrected counts.

### FR-4: Update affected unit tests
Existing tests in `MarketingInvoiceImportServiceTests` assume per-record `SaveChangesAsync` and must be updated to the batched contract.

**Acceptance criteria:**
- `ImportAsync_NewTransactions_ArePersistedAndCounted` changes its verification from `SaveChangesAsync ... Times.Exactly(2)` to `Times.Once`; `Imported == 2` assertion is unchanged.
- `ImportAsync_DuplicateTransaction_IsSkipped` continues to pass; add verification that `SaveChangesAsync` is `Times.Never` when nothing is staged.
- `ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun` continues to pass (`Imported == 1`, `Failed == 1`).
- A new test covers FR-3: when the post-loop `SaveChangesAsync` throws, `Imported == 0` and `Failed` equals the number of staged records, and `ImportAsync` does not throw.
- A new test covers the empty-input / all-skipped case: `SaveChangesAsync` is `Times.Never`.

## Non-Functional Requirements

### NFR-1: Performance
For an import run with N new transactions, the number of `SaveChangesAsync` round-trips drops from N to 1 (or 0 when nothing is new). `ExistsAsync` continues to be one query per source transaction — unchanged by this work. No regression in the import job's wall-clock time; expected improvement proportional to N.

### NFR-2: Security
No change to the security posture. No new inputs, endpoints, secrets, or auth surface. The service remains an internal job-invoked component. Exception logging must not include raw connection strings or SQL text (existing `LogError` calls already pass only structured properties).

## Data Model
Unchanged. The affected entity is `ImportedMarketingTransaction` (`Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`): `Id` (int identity), `TransactionId`, `Platform`, `Amount`, `TransactionDate`, `ImportedAt`, `IsSynced`, `ErrorMessage`. No schema change, no migration.

## API / Interface Design
No public API or contract change.
- `IImportedMarketingTransactionRepository` (`ExistsAsync`, `AddAsync`, `GetUnsyncedAsync`, `SaveChangesAsync`) is unchanged.
- `MarketingInvoiceImportService.ImportAsync(DateTime from, DateTime to, CancellationToken ct)` signature and return type (`MarketingImportResult`) are unchanged.
- The only changes are internal to `ImportAsync`: loop body removes the inline `SaveChangesAsync`; a guarded single `SaveChangesAsync` is added after the loop.

## Dependencies
- EF Core change tracking via `ApplicationDbContext` / `BaseRepository<ImportedMarketingTransaction, int>` — already in place.
- xUnit + Moq test stack for `MarketingInvoiceImportServiceTests` — already in place.
- No new external services or libraries.

## Out of Scope
- Introducing an explicit database transaction / `IDbContextTransaction` wrapper around the import.
- Adding a run-id or batch marker column to distinguish records by import run.
- Changing duplicate-detection (`ExistsAsync`) into a batched/bulk lookup.
- Bulk-insert libraries (e.g. `EFCore.BulkExtensions`).
- Changes to the import scheduling job, lookback window, or the sync (`GetUnsyncedAsync`) path.
- Reworking the `MarketingTransaction` source contract.

## Open Questions
None.

## Status: COMPLETE
