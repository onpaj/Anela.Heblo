## Goal
Add four unit tests to `ImportBankStatementHandlerTests` covering coverage gaps in `ImportBankStatementHandler`.

See `artifacts/feat-3411/task-plan.r1.md` for full details.

Tests to add:
1. `Handle_LogsStaleWarning_WhenWatermarkIsStale`
2. `Handle_DoesNotLogWarning_WhenWatermarkIsFresh`
3. `Handle_FallsBackToInsert_WhenRetryStatementNotFoundInDb`
4. `Handle_RecordsImportServiceError_WhenImportServiceReturnsFailed`

File: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`
