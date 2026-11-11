### task: ledger-service-tests
Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Ledger/LedgerServiceTests.cs` (xUnit/Moq style used in that project; fake `TimeProvider`, real `MemoryCache`, mocked `ILedgerClient`, AutoMapper config as other tests do).
- N=10 parallel identical calls -> `ILedgerClient.GetAsync` called exactly once; all callers get the same list.
- Failing/canceled shared request is not cached; subsequent call retries (client called again).
- Caller cancels its token while another caller waits: canceling caller gets `OperationCanceledException`, the other caller still receives the result.
- TTL: window ending before current-month start uses closed TTL, window touching current month uses open TTL (advance fake time to prove expiry boundaries).
- `FlexiLedger.QueryCompleted` logged on success with rows/span (use a capturing logger); timeout and caller-cancel exceptions rethrown.
- FR-5: add a test in `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/` (or beside `FinancialAnalysisServiceTests.cs`) wiring real `LedgerService` over a counting fake `ILedgerClient` and asserting calls <= distinct (window, prefix, department) tuples for a `FinancialAnalysisService` run. If the existing service structure makes this impractical, assert the same through repeated `LedgerService.GetLedgerItems` calls and state why in the test file.
- Verify: `dotnet test backend --filter "LedgerService|FinancialAnalysis"` passes.

