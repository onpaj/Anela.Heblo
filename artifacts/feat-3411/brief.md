## Module / File
`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`

## Coverage
Line coverage: 33.1% (filter threshold: 60%)
11 existing tests; 1 integration test file also exists.

## What's not tested

**`ProcessStatementAsync` retry path (`isRetry = true`)**:
- When a statement was previously imported with a non-Success result (it exists in `existingTransfers` with a failed status), the handler sets `isRetry = true` and calls `UpsertExistingAsync` instead of `InsertNewAsync`. No test exercises this path. A bug here (e.g. `UpsertExistingAsync` not updating the result status) means failed bank statements can never be corrected on re-import — they stay in a permanent error state.

**Stale watermark warning path**:
- When `state.LastValidImportDate` has a value and `daysBehind > _watermarkOptions.StaleWarningDays`, a warning is logged. While the warning is non-functional (import proceeds), it also means the `StaleWarningDays` setting itself has never been exercised — a misconfiguration that sets it to 0 would spam warnings on every import run.

**`state.RecordFailure` path on exception**:
- The outer `catch` block records a failure on the state object and upserts it even when the main try-block throws. No test verifies that a mid-import exception (e.g. bank client throws) results in the state being persisted as failed.

## Why it matters
The retry path is the mechanism for recovering from a failed bank import. If `UpsertExistingAsync` has a bug, every statement that fails once becomes permanently stuck, and the stale-check means drift goes undetected until it is manually spotted.

## Suggested approach
- Seed `mockStateRepository` with a `BankImportState` that has a `LastValidImportDate` older than `StaleWarningDays` and verify a warning is logged.
- Pre-populate `existingTransfers` with a `StatementId → ImportStatus.UnknownError` entry and verify `UpsertExistingAsync` is called and the returned DTO reflects the updated result.
- Throw from `_mockBankClient.GetStatementsAsync` and assert `_mockStateRepository.UpsertAsync` is still called with a failure state. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-06-29. Based on CI run #28295125598 (23c3b5d571c976074ee31869c96e29487098040c)._
