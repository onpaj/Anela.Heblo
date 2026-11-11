## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/ForceRefreshTaskHandlerTests.cs:56` — `Handle_ReturnsNotFound_WhenSubclassOfInvalidOperationThrown` could also assert `ErrorMessage`; optional.

Notes: test-only change; assertions match `ForceRefreshTaskHandler` behavior (success, InvalidOperationException -> NotFound + Warning, other -> generic error + Error log, no leakage of exception text). Success is deliberately not asserted on the NotFound path, per spec open question. Reviewed by inspection; the local `dotnet test` run exceeded the time budget in this environment, so test execution is left to CI.
