# Code Review: add-run-hydration-tier-handler-tests

## Summary
The new `RunHydrationTierHandlerTests.cs` covers all six required test scenarios and matches `RunHydrationTierHandler.Handle(...)`'s actual behavior line-for-line (confirmed by reading the handler source). It follows the mandated `Mock<ILogger<T>>` + `VerifyLogged` pattern from arch-review Decision 1 (not `NullLogger`), mirrors the `MakeSut()` tuple-factory convention from `GetOrderTrackingNumberHandlerTests`, and the commit (`498863d`) touches only the new test file — no production code changes. Independent `dotnet test` execution in this review sandbox did not finish within the available time (large solution, slow incremental build in this environment); verification below is based on rigorous static comparison against the handler source plus the implementer's own reported local run (6/6 passed).

## Review Result: PASS

### task: add-run-hydration-tier-handler-tests
**Status:** PASS

## Docs to Update
None — this is test-only work with no doc-facing behavior change.

## Overall Notes
- File location/namespace: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs`, namespace `Anela.Heblo.Tests.Application.BackgroundRefresh` — matches spec exactly.
- `MakeSut()` returns `(Sut, Registry, Logger)` using `Mock<IBackgroundRefreshTaskRegistry>` and `Mock<ILogger<RunHydrationTierHandler>>`, satisfying arch-review Decision 1 (hard requirement, not `NullLogger`).
- `VerifyLogged(logger, level, times)` is a byte-for-byte match of the pattern in `GetPackageLabelPdfHandlerTests.cs` (adapted to a static method taking the mock as a parameter, which is a reasonable, harmless adaptation for a test-per-`MakeSut()` design).
- `MakeTaskConfig(taskId, hydrationTier, enabled)` builds `RefreshTaskConfiguration` with all four `required` properties plus `HydrationTier`, matching the class definition in `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskConfiguration.cs`.
- All 6 required `[Fact]`s are present and each asserts on the correct response fields and mock interactions:
  - `Handle_ReturnsNotFound_WhenNoEnabledTasksInTier` — empty registry, `NotFound=true`, `ErrorMessage` contains `"2"`, `TaskCount=0`, `Cancelled=false`, `ForceRefreshAsync` never called.
  - `Handle_ReturnsNotFound_WhenTasksInTierAreAllDisabled` — two `Enabled=false` tasks in-tier, same outcome, exercises the `t.Enabled` filter specifically.
  - `Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully` — 2 in-tier + 1 other-tier (99) task; `TaskCount=2`; `ForceRefreshAsync` verified `Times.Once` for each in-tier `TaskId` and `Times.Never` for the other-tier id (functionally equivalent to spec's suggested `Times.Exactly(2)` — two distinct-id `Times.Once` checks together prove exactly two calls happened, one per task, none skipped/duplicated); `VerifyLogged(Information, Once)` present.
  - `Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown` — `ForceRefreshAsync` throws `OperationCanceledException`, caught by the handler's `catch (OperationCanceledException)`, `Cancelled=true`, `Success=true` (default, unmodified — matches handler, which does not set `Success=false` on cancellation), no exception propagates.
  - `Handle_ReturnsCancelled_WhenTokenAlreadyCancelled` — real pre-cancelled `CancellationTokenSource.Token` (not mocked), asserts `Cancelled=true` and `ForceRefreshAsync` never invoked, correctly verifying `cancellationToken.ThrowIfCancellationRequested()` fires before the first loop iteration's call.
  - `Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException` — `InvalidOperationException("boom")`, asserts `Success=false`, exact `ErrorMessage` match against the literal `"An unexpected error occurred during tier hydration"`, `Cancelled=false`, `NotFound=false`, `VerifyLogged(Error, Once)`, and explicitly asserts `"boom"` does not leak into `ErrorMessage`.
- NFR-3 (isolation/determinism): every `[Fact]` calls `MakeSut()` fresh; no shared mutable state, no `Task.Delay`, no real timers — confirmed by reading the full file.
- No `.csproj` changes; `<Using Include="Xunit" />` is already a global using in `Anela.Heblo.Tests.csproj`, so the omission of an explicit `using Xunit;` is correct and consistent with the project's global-usings convention (the `GetOrderTrackingNumberHandlerTests.cs` template predates or simply retains a redundant explicit using — no functional difference).
- Handler source (`RunHydrationTierHandler.cs`) matches the "ground truth" block quoted in the task context exactly; no discrepancy was found, so the implementer's decision not to touch production code is correct.
- Independent verification caveat: `dotnet test --filter "FullyQualifiedName~RunHydrationTierHandlerTests"` was invoked in this review session but had not completed after ~10 minutes of wall-clock time in the sandbox (large solution, cold-ish incremental build); this appears to be an environment/resource characteristic of this particular review sandbox rather than a defect in the test code. The implementation's own report states a local run of 6/6 passed with an isolated failure set (76 pre-existing Docker/Testcontainers-dependent integration test failures, all unrelated to `BackgroundRefresh`). Given the exhaustive line-by-line match against the handler's actual control flow, this is not treated as a blocking concern.
