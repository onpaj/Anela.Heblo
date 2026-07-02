# Task Plan: Bank ImportBankStatementHandler coverage gaps

### task: add-missing-tests

**Goal**: Add four unit tests to `ImportBankStatementHandlerTests` covering the three coverage gaps identified in feat-3411.

**File**: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

**Tests to add** (append after the last existing test):

1. `Handle_LogsStaleWarning_WhenWatermarkIsStale`
   - State with `LastValidImportDate = DateTime.UtcNow.AddDays(-10)` (10 days ago)
   - Default handler: `StaleWarningDays = 3` → `10 > 3` → warning triggers
   - Assert `_mockLogger.Log(LogLevel.Warning, ...)` called once

2. `Handle_DoesNotLogWarning_WhenWatermarkIsFresh`
   - State with `LastValidImportDate = DateTime.UtcNow.AddDays(-1)` (1 day ago)
   - Default handler: `StaleWarningDays = 3` → `1 ≤ 3` → no warning
   - Assert `_mockLogger.Log(LogLevel.Warning, ...)` never called

3. `Handle_FallsBackToInsert_WhenRetryStatementNotFoundInDb`
   - `existingTransfers` has `"RETRY" → ProcessingError` (so `isRetry = true`)
   - `GetByTransferIdAsync("RETRY")` returns `null`
   - `UpsertExistingAsync` falls back to `InsertNewAsync`
   - Assert `AddAsync` called once, `UpdateAsync` never called

4. `Handle_RecordsImportServiceError_WhenImportServiceReturnsFailed`
   - `ImportStatementAsync` returns `Result<bool>.Failure("import-error")`
   - Handler sets `resultStatus = "import-error"`
   - `AddAsync` called with `ImportResult = "import-error"`
   - Assert `response.ErrorCount == 1`, `response.SuccessCount == 0`

**Validation**: Run `dotnet test --filter "FullyQualifiedName~ImportBankStatementHandlerTests"` — all tests must pass.
