# Implementation: add-run-hydration-tier-handler-tests

## What was implemented

Added a new xUnit test class `RunHydrationTierHandlerTests` that exercises all four response
paths of `RunHydrationTierHandler.Handle(...)`: not-found (empty tier and all-disabled-tasks
sub-case), successful multi-task hydration, cancellation (both a thrown
`OperationCanceledException` mid-loop and a pre-cancelled token before the loop starts), and an
unexpected-exception failure path. The exception path also verifies `ILogger.LogError` was
invoked via a `Mock<ILogger<RunHydrationTierHandler>>` + local `VerifyLogged` helper (per
arch-review Decision 1 — no `NullLogger` used), and the success path verifies `LogInformation`
was invoked once. This is test-only work; no production code was changed — the handler behaved
exactly as documented in the task context, so no defect fix was needed.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs` — new file, new test class with `MakeSut()` tuple-factory (`Mock<IBackgroundRefreshTaskRegistry>` + `Mock<ILogger<RunHydrationTierHandler>>`), a `VerifyLogged` helper, a `MakeTaskConfig` helper for building `RefreshTaskConfiguration` instances, and 6 `[Fact]` test methods.

## Tests

`RunHydrationTierHandlerTests.cs` covers:
- `Handle_ReturnsNotFound_WhenNoEnabledTasksInTier` — empty registry → `NotFound=true`, `ErrorMessage` contains the tier number, `TaskCount=0`, `Cancelled=false`, `ForceRefreshAsync` never called.
- `Handle_ReturnsNotFound_WhenTasksInTierAreAllDisabled` — tasks exist for the tier but all `Enabled=false` → same `NotFound` outcome, exercising the `.Where(... && t.Enabled)` filter specifically.
- `Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully` — 2 enabled tasks in-tier + 1 enabled task in a different tier → `TaskCount=2`, `Success=true`, `ForceRefreshAsync` called once per in-tier task id and never for the other-tier task, `LogInformation` verified once.
- `Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown` — `ForceRefreshAsync` throws `OperationCanceledException` → caught, `Cancelled=true`, `Success=true`, no unhandled exception propagates.
- `Handle_ReturnsCancelled_WhenTokenAlreadyCancelled` — a real, pre-cancelled `CancellationTokenSource.Token` passed in → `ThrowIfCancellationRequested()` fires before the first `ForceRefreshAsync` call, `Cancelled=true`, `ForceRefreshAsync` never called.
- `Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException` — `ForceRefreshAsync` throws `InvalidOperationException("boom")` → `Success=false`, `ErrorMessage` is the exact generic message (no leakage of `"boom"`), `Cancelled=false`, `NotFound=false`, `LogError` verified once.

## How to verify

```
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RunHydrationTierHandlerTests"
```

All 6 new tests pass (verified locally: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`).

Full suite was also run (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build`):
5696 passed, 76 failed, 4 skipped. All 76 failures are pre-existing integration tests that
require Docker/Testcontainers (Postgres containers for Bank/Leaflet/KnowledgeBase/Photobank/etc.
repository integration tests) and fail with `Docker is either not running or misconfigured` in
this sandbox — unrelated to this change and not present anywhere near
`BackgroundRefresh`/`RunHydrationTier`. No regressions were introduced by this change.

## Notes

- `dotnet build` on the test project succeeded with 0 errors; the 237 warnings present are all
  pre-existing (nullable-reference warnings in unrelated test files), none originating from the
  new file.
- `dotnet format Anela.Heblo.sln --include backend/test/.../RunHydrationTierHandlerTests.cs` ran
  clean with no changes needed.
- No `.csproj` changes were required — the test project already references
  `Anela.Heblo.Application`, `Anela.Heblo.Xcc`, `Moq`, `FluentAssertions`, and `Xunit` is a global
  using, matching the sibling `GetOrderTrackingNumberHandlerTests.cs` convention (no explicit
  `using Xunit;`).
- No production code changes were made — `RunHydrationTierHandler.cs` behaved exactly as
  documented in the task context for every test case; no defect was found.

## PR Summary

Adds `RunHydrationTierHandlerTests.cs` under
`backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/`, closing the coverage gap
flagged in CI run #28968007617 (17.9% line coverage on `RunHydrationTierHandler.cs`). The new
test class covers all four response branches of `Handle(...)`: not-found (including the
all-disabled-tasks sub-case that exercises the `Enabled` filter), successful multi-task
hydration with per-task `ForceRefreshAsync` verification, cancellation via both a thrown
`OperationCanceledException` and a pre-cancelled token, and an unexpected-exception failure path
with exact error-message assertion and no leakage of the original exception message. Per the
arch-review's mandatory amendment (Decision 1), logger behavior is verified using
`Mock<ILogger<RunHydrationTierHandler>>` + a `VerifyLogged` helper rather than `NullLogger`,
confirming both the informational log on tier-hydration start and the error log on failure.

### Changes
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs` (new, 156 lines)

## Status
DONE
