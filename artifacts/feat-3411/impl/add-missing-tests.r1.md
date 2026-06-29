# Implementation: add-missing-tests r1

## Changes made

**File**: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

Added `using Microsoft.Extensions.Logging;` import and four new tests:

1. **Handle_LogsStaleWarning_WhenWatermarkIsStale** — Seeds state with `LastValidImportDate = DateTime.UtcNow.AddDays(-10)` (10 days ago). Default `StaleWarningDays = 3`. Asserts `_mockLogger.Log(LogLevel.Warning, ...)` is called once with message containing "stale". Covers lines 74-78 of handler (inner-if true branch).

2. **Handle_DoesNotLogWarning_WhenWatermarkIsFresh** — Seeds state with `LastValidImportDate = DateTime.UtcNow.AddDays(-1)` (1 day ago). Default `StaleWarningDays = 3`. Asserts no `LogLevel.Warning` is logged. Covers the inner-if false branch of lines 75-78.

3. **Handle_FallsBackToInsert_WhenRetryStatementNotFoundInDb** — Sets `isRetry = true` via `existingTransfers["RETRY"] = ProcessingError` but makes `GetByTransferIdAsync` return null. Asserts `AddAsync` called once, `UpdateAsync` never called. Covers lines 217-218 of `UpsertExistingAsync`.

4. **Handle_RecordsImportServiceError_WhenImportServiceReturnsFailed** — Makes `ImportStatementAsync` return `Result<bool>.Failure("import-error")`. Asserts `AddAsync` is called with `ImportResult = "import-error"` and response has `ErrorCount = 1`. Covers line 173 false branch of `ProcessStatementAsync`.

## Test results
`dotnet test --filter "FullyQualifiedName~ImportBankStatementHandlerTests"` → 15 passed, 0 failed
