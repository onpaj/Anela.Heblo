# Specification: Bank ImportBankStatementHandler coverage gaps

## Summary
Add unit tests to `ImportBankStatementHandlerTests` covering three branches not currently exercised: the stale-watermark warning log (both the triggered and the non-triggered branches), the `UpsertExistingAsync` null-fallback path (where the DB row is unexpectedly absent), and the import-service business-failure path (where `ImportStatementAsync` returns `IsSuccess = false`).

## Background
A weekly coverage-gap routine flagged `ImportBankStatementHandler` at 33.1% line coverage. While several tests were added in PR #3329 (retry path, exception catch), three distinct branches remain untested as of the coverage run at commit 23c3b5d.

## Functional Requirements

### FR-1: Stale watermark warning — triggered
When `state.LastValidImportDate` is set and `daysBehind > StaleWarningDays`, the handler logs a Warning. A test must seed state with a date older than the threshold and assert `ILogger.Log(LogLevel.Warning, ...)` is called exactly once.

**Acceptance criteria:**
- `_mockLogger.Verify(Log(LogLevel.Warning, ...), Times.Once)` passes when `daysBehind > StaleWarningDays`.

### FR-2: Stale watermark warning — not triggered
When `state.LastValidImportDate` is set but `daysBehind <= StaleWarningDays`, no Warning is logged.

**Acceptance criteria:**
- `_mockLogger.Verify(Log(LogLevel.Warning, ...), Times.Never)` passes when `daysBehind <= StaleWarningDays`.

### FR-3: UpsertExistingAsync null fallback
When a statement is marked as a retry (`isRetry = true`) but `GetByTransferIdAsync` returns null (race / data loss), `UpsertExistingAsync` falls back to `InsertNewAsync`.

**Acceptance criteria:**
- `_mockRepository.Verify(AddAsync(...), Times.Once)` passes.
- `_mockRepository.Verify(UpdateAsync(...), Times.Never)` passes.

### FR-4: Import service business failure
When `ImportStatementAsync` returns `Result<bool>.Failure("error-msg")` (not an exception), the handler sets `resultStatus = "error-msg"` and persists it.

**Acceptance criteria:**
- `AddAsync` is called with `ImportResult == "error-msg"`.
- The response `ErrorCount == 1` and `SuccessCount == 0`.

## Non-Functional Requirements

### NFR-1: Test isolation
Each new test must be self-contained. No shared mutable state beyond the constructor-initialized mocks.

### NFR-2: Deterministic dates
Tests that involve `daysBehind` must use relative dates (`DateTime.UtcNow.AddDays(-N)`) to remain correct regardless of when the suite runs.

## Data Model
No changes to production domain models. Tests operate on existing domain types:
- `BankImportState` — seeded via `RecordSuccess(watermark, ...)`.
- `BankImportWatermarkOptions` — `StaleWarningDays` controls the threshold.
- `Result<bool>` — `Failure("msg")` constructor exercises the business-error path.

## API / Interface Design
No API changes. Unit test additions only.

## Dependencies
- Existing `Mock<ILogger<ImportBankStatementHandler>>` in test constructor.
- Moq 4.20.72 `It.Is<It.IsAnyType>` pattern for logger verification (as used in `BackgroundRefreshSchedulerServiceTests`).
- `Result<bool>.Failure(string)` factory method from `Domain.Shared`.

## Out of Scope
- Integration tests.
- Changes to production handler code.
- E2E tests.

## Open Questions
None.

## Status: COMPLETE
