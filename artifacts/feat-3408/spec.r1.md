# Specification: Fix SaveChangesAsync Exception Swallowing in MarketingInvoiceImportService

## Summary

`MarketingInvoiceImportService.ImportAsync` silently swallows exceptions thrown by `SaveChangesAsync`, causing a batch-level DB flush failure to be reported as a successful Hangfire job execution with `Failed=N`. This directly contradicts the documented architectural intent in `ImportMarketingInvoicesHandler`, which states that import-time exceptions must propagate so Hangfire can schedule a retry. The fix is a one-line `throw;` addition in the `SaveChangesAsync` catch block, accompanied by a test update to reflect the corrected contract.

## Background

The MarketingInvoices module runs two Hangfire recurring jobs — `MetaAdsInvoiceImportJob` (cron `0 6,18 * * *`) and `GoogleAdsInvoiceImportJob` (cron `15 6,18 * * *`) — that each dispatch an `ImportMarketingInvoicesRequest` via MediatR. The handler routes to `MarketingInvoiceImportService.ImportAsync`, which fetches transactions from a platform source, stages them via `IImportedMarketingTransactionRepository.AddAsync`, and flushes the unit of work with a single `SaveChangesAsync` call after the loop.

The service uses two distinct catch boundaries:

1. **Per-transaction catch** (lines 83–90): Catches `AddAsync` failures on individual transactions. This is intentional — a bad row should not abort the entire run. Affected transactions are counted as `Failed` and the loop continues.
2. **Batch flush catch** (lines 100–108): Catches `SaveChangesAsync` failures after all transactions have been staged. This is the bug. A flush failure means **no** transaction from the run was persisted, and the failure is typically transient (DB connectivity blip, connection pool exhaustion, deadlock). The current code logs the error and returns a result object with `Failed=N`, `Imported=0` — the exception is not rethrown.

Because the exception is swallowed, `ImportMarketingInvoicesHandler` never sees it, the job logs a completion message at `Information` level, and Hangfire records a success. The architectural comment at handler lines 50–52 explicitly documents that this propagation path must work; it does not.

A transient flush failure silently drops up to the entire 7-day lookback window of transactions for a run. The next opportunity to recover them is the subsequent scheduled execution — up to 12 hours later. There is no alert, no Hangfire failure record, and no operator visibility.

## Functional Requirements

### FR-1: Rethrow SaveChangesAsync exceptions after logging

When `SaveChangesAsync` throws, `MarketingInvoiceImportService.ImportAsync` must:
1. Log the error at `Error` level (already done — no change needed here).
2. Increment `result.Failed` by `stagedCount` (already done — no change needed here).
3. Rethrow the original exception using `throw;` so the exception propagates to `ImportMarketingInvoicesHandler` and from there to the Hangfire job's catch block.

**Acceptance criteria:**
- When `SaveChangesAsync` throws any exception, `ImportAsync` rethrows it (the exception propagates out of `ImportAsync`).
- The `LogError` call for the flush failure still executes before the rethrow.
- The per-transaction catch boundary (lines 83–90) is unaffected — `AddAsync` failures still do not abort the run.
- `MetaAdsInvoiceImportJob.ExecuteAsync` and `GoogleAdsInvoiceImportJob.ExecuteAsync` receive the propagated exception in their existing `catch (Exception ex)` blocks, log it at `Error` level, and rethrow it so Hangfire records a failure and schedules a retry.

### FR-2: Update the existing test that asserts the swallowing behavior

The test `ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow` in `MarketingInvoiceImportServiceTests` currently asserts `DoesNotThrow`. This assertion encodes the buggy behavior. It must be replaced with a test that asserts the exception is propagated.

**Acceptance criteria:**
- The test is renamed to `ImportAsync_FinalSaveChangesThrows_Rethrows` (or equivalent that communicates the correct contract).
- The test body uses `await Assert.ThrowsAsync<InvalidOperationException>(...)` (matching the mock's thrown type) instead of a plain `await _service.ImportAsync(...)`.
- The `result.Failed == 2` assertion is removed (the exception propagates before a result is returned; there is no return value to assert on).
- The `result.Imported == 0` and `result.Skipped == 0` assertions are removed for the same reason.
- The `SaveChangesAsync` mock verification (`Times.Once`) is preserved.
- All other existing tests in `MarketingInvoiceImportServiceTests` continue to pass without modification.

### FR-3: Add a service-level test for the rethrow behavior

A new test must explicitly document that the exception type is preserved through the rethrow (i.e., `throw;` is used, not `throw ex;`).

**Acceptance criteria:**
- A test named `ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved` (or equivalent) sets up `SaveChangesAsync` to throw a specific typed exception (e.g., `InvalidOperationException` with a known message).
- `Assert.ThrowsAsync<InvalidOperationException>` confirms the exact type propagates.
- This test is in `MarketingInvoiceImportServiceTests`.

## Non-Functional Requirements

### NFR-1: No behavior change for the happy path

The fix must not alter any code path that does not involve a `SaveChangesAsync` exception. Specifically: normal import, per-transaction failures, empty input, duplicate detection, and missing-currency handling must all behave identically to before.

### NFR-2: Minimal diff

The production code change is a single line (`throw;`) inside the existing catch block. No refactoring of surrounding logic, no new abstractions, no changes to logging format, no changes to `MarketingImportResult`.

### NFR-3: Build must pass

`dotnet build` must succeed after the change. `dotnet format` must report no violations.

### NFR-4: All tests must pass

All tests in the solution must pass after the change, including the updated and new tests in `MarketingInvoiceImportServiceTests`.

## Data Model

No data model changes. `MarketingImportResult` (`Imported`, `Skipped`, `Failed`) is unchanged.

## API / Interface Design

No API or interface changes. `IMarketingInvoiceImportService.ImportAsync` signature is unchanged. The behavioral contract changes: callers must now handle (or propagate) exceptions from `ImportAsync` when `SaveChangesAsync` fails. The existing job catch-rethrow pattern already handles this correctly — no job-level code changes are needed.

## Dependencies

- `MarketingInvoiceImportService` — production fix (`throw;` in `SaveChangesAsync` catch).
- `MarketingInvoiceImportServiceTests` — test update (rename + fix one test, add one test).
- No other files require changes.

## Files to Modify

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | Add `throw;` at line 108, after `result.Failed += stagedCount;` |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Rename and fix `ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow`; add `ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved` |

## Out of Scope

- Changes to `MetaAdsInvoiceImportJob` or `GoogleAdsInvoiceImportJob` — they already rethrow correctly.
- Changes to `ImportMarketingInvoicesHandler` — it already intentionally does not catch.
- Alerting, monitoring dashboards, or Hangfire dashboard configuration.
- Retry policy configuration (Hangfire defaults apply).
- Any changes to the per-transaction catch block (lines 83–90) — that behavior is intentional and correct.
- Adding a `stagedCount == 0` fast-exit path — not requested and out of scope.
- Changes to any other module or service.

## Open Questions

None.

## Status: COMPLETE
