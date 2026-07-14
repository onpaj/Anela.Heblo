# Specification: Unit tests for RunHydrationTierHandler

## Summary
Add a focused unit test suite for `RunHydrationTierHandler` (`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierHandler.cs`) that exercises its four structurally distinct response paths — no enabled tasks in tier, successful hydration, cancellation, and unexpected exception — none of which are currently covered by any test. This is a test-coverage-gap fix, not a behavior change: no production code is expected to change unless a test reveals an actual defect.

## Background
A weekly coverage-gap routine flagged `RunHydrationTierHandler.cs` at 17.9% line coverage against a 60% threshold (CI run #28968007617). The handler is a small MediatR request handler with no existing dedicated test file — there is no `Application/BackgroundRefresh` folder under `backend/test/Anela.Heblo.Tests` at all (siblings `GetAllHistoryHandler`, `GetTaskStatusHandler`, `ForceRefreshTaskHandler`, `GetBackgroundRefreshTasksHandler`, `GetTaskHistoryHandler` also appear untested). The risk called out in the brief: callers (frontend and scheduled jobs) branch on `NotFound`, `Cancelled`, and `Success` to decide what to display, so a mislabeled flag (e.g. `Cancelled` not set on a cancelled run, or a missing `await` on `ForceRefreshAsync`) would ship silently and mislead the UI.

## Functional Requirements

### FR-1: Test project and file placement
Create a new test class `RunHydrationTierHandlerTests` in a new folder `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/`, mirroring the existing convention used for sibling handler tests (e.g. `backend/test/Anela.Heblo.Tests/Application/Packaging/GetOrderTrackingNumberHandlerTests.cs`). Namespace: `Anela.Heblo.Tests.Application.BackgroundRefresh`.

**Acceptance criteria:**
- File exists at `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs`.
- Class compiles within the existing `Anela.Heblo.Tests` project (no new `.csproj` needed — confirmed the test project already references `Anela.Heblo.Application` and `Anela.Heblo.Xcc`, and already uses xUnit + Moq + FluentAssertions elsewhere in this project).
- Test class and helper follow the existing local convention: a private static `MakeSut()` factory returning the handler under test plus its mocks, matching the pattern in `GetOrderTrackingNumberHandlerTests`.

### FR-2: Cover "no enabled tasks in tier" path
Verify that when `IBackgroundRefreshTaskRegistry.GetRegisteredTasks()` returns no tasks matching the requested tier (either because none exist, or because matching tasks exist but are `Enabled = false`), the handler returns `NotFound = true` and a non-empty `ErrorMessage` containing the requested tier number, without calling `ForceRefreshAsync`.

**Acceptance criteria:**
- Test `Handle_ReturnsNotFound_WhenNoEnabledTasksInTier` (or equivalent name) asserts `response.NotFound == true`.
- Asserts `response.ErrorMessage` is non-null/non-empty and contains the tier number requested (matches the handler's `$"No enabled tasks found for tier {request.Tier}"` format).
- Asserts `response.TaskCount == 0` (default) and `response.Cancelled == false`.
- Verifies `taskRegistry.ForceRefreshAsync(...)` was never invoked (`Times.Never`).
- Include at least one sub-case where tasks exist for the tier but all have `Enabled = false`, to prove the `.Where(t => ... && t.Enabled)` filter is exercised, not just an empty registry.

### FR-3: Cover "all tasks complete successfully" path
Verify that when the registry returns 2 enabled tasks for the requested tier and `ForceRefreshAsync` completes without throwing for each, the handler returns `TaskCount = 2`, with `NotFound`, `Cancelled` false and `Success` true (inherited default).

**Acceptance criteria:**
- Test `Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully` asserts `response.TaskCount == 2`.
- Asserts `response.NotFound == false`, `response.Cancelled == false`, `response.Success == true`.
- Verifies `ForceRefreshAsync` was called once per task (`Times.Exactly(2)`), including verifying it was called with each of the two distinct `TaskId` values (proves no task was skipped or double-invoked).
- Verifies tasks are processed only for the requested tier: seed the registry with a task belonging to a different tier and assert it is not included in `TaskCount` and `ForceRefreshAsync` is not called for it.

### FR-4: Cover cancellation path
Verify that when `ForceRefreshAsync` throws `OperationCanceledException` (simulating cancellation mid-loop), the handler catches it and returns `Cancelled = true`, with `Success` remaining true (per current implementation — the handler does not set `Success = false` on cancellation) and no exception propagating to the caller.

**Acceptance criteria:**
- Test `Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown` sets up the mocked registry with at least one enabled task for the tier and configures `ForceRefreshAsync` to throw `OperationCanceledException`.
- Asserts `response.Cancelled == true`.
- Asserts no exception is thrown out of `Handle(...)` (the `await sut.Handle(...)` call completes normally).
- Additionally cover the `cancellationToken.ThrowIfCancellationRequested()` branch directly: seed 2+ enabled tasks, pass an already-cancelled `CancellationToken` (via `CancellationTokenSource` cancelled before the call), and assert `response.Cancelled == true` with `ForceRefreshAsync` never called (since the token check happens before the first call in the loop).
- Note for implementer: `OperationCanceledException` and its subclass `TaskCanceledException` are both caught by the existing `catch (OperationCanceledException)` clause; a `ThrowsAsync(new OperationCanceledException())` mock setup is sufficient and simpler than driving real token cancellation through the mock, but the "already-cancelled token" sub-case must exercise the real `CancellationToken`, not a mocked exception.

### FR-5: Cover unexpected exception path
Verify that when `ForceRefreshAsync` throws a non-cancellation exception (e.g. `InvalidOperationException`), the handler catches it, logs an error, and returns `Success = false` with a fixed, non-empty `ErrorMessage` ("An unexpected error occurred during tier hydration"), without leaking the original exception's message or propagating the exception.

**Acceptance criteria:**
- Test `Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException` sets up `ForceRefreshAsync` to throw a generic exception (e.g. `InvalidOperationException("boom")`).
- Asserts `response.Success == false`.
- Asserts `response.ErrorMessage` is non-empty (exact string match against the known literal is acceptable and preferred, since the brief explicitly calls out this literal as a regression risk).
- Asserts `response.Cancelled == false` and `response.NotFound == false`.
- Verifies the logger was invoked at `LogError` level (using a mocked/null `ILogger<RunHydrationTierHandler>` — assert via `Mock<ILogger<...>>.Verify(...)` if the project's existing tests exercise logger verification elsewhere, otherwise a `NullLogger<RunHydrationTierHandler>.Instance` is acceptable per FR-6 and this assertion may be omitted if the codebase has no established logger-verification pattern to follow).

### FR-6: Test doubles and setup conventions
Use `Moq` for `IBackgroundRefreshTaskRegistry` (interface, easily mockable) and `Microsoft.Extensions.Logging.Abstractions.NullLogger<RunHydrationTierHandler>.Instance` for the logger, matching the pattern already used in `GetOrderTrackingNumberHandlerTests`. Build `RefreshTaskConfiguration` instances directly (it is a plain class with `required` init-only properties: `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier`) rather than mocking it.

**Acceptance criteria:**
- No new test infrastructure, base classes, or fixtures are introduced beyond what the file itself needs.
- `RefreshTaskConfiguration` objects used in tests set `HydrationTier` to match (or deliberately mismatch, for the "other tier" sub-case in FR-3) the requested tier, and set `Enabled` explicitly (true/false) per test case.
- `TimeSpan` fields on `RefreshTaskConfiguration` (`InitialDelay`, `RefreshInterval`) may be set to `TimeSpan.Zero` or any arbitrary value since they are irrelevant to the handler's logic.

## Non-Functional Requirements

### NFR-1: Performance
Tests must run fully in-memory with mocked dependencies; no I/O, no real delays, no real cancellation timers. Full suite for this file should execute in well under 1 second.

### NFR-2: Security
N/A — no security-sensitive surface in this handler or its tests.

### NFR-3: Isolation and determinism
Tests must not depend on execution order, shared mutable state, wall-clock time, or `Task.Delay`. Each test builds its own registry mock and task list.

## Data Model
N/A — this is a test-coverage addition; no changes to `RunHydrationTierRequest`, `RunHydrationTierResponse`, or `RefreshTaskConfiguration`. For reference, the response shape under test:
- `RunHydrationTierResponse : BaseResponse` — `Success` (bool, default `true`, inherited), `NotFound` (bool), `Cancelled` (bool), `ErrorMessage` (string?), `TaskCount` (int).

## API / Interface Design
N/A — no interface, controller, or contract changes. The handler is invoked internally via MediatR from `BackgroundRefreshController`; this task does not touch the controller or the HTTP surface.

## Dependencies
- Existing NuGet packages already used by the test project: `xunit`, `Moq`, `FluentAssertions`, `Microsoft.Extensions.Logging.Abstractions`.
- `IBackgroundRefreshTaskRegistry` and `RefreshTaskConfiguration` from `Anela.Heblo.Xcc.Services.BackgroundRefresh` (already referenced by the test project via existing `Xcc/BackgroundRefresh` tests).
- No new dependencies required.

## Out of Scope
- Testing `RunHydrationTierRequestValidator` (the `Tier > 0` FluentValidation rule) — not part of the flagged handler and has no existing coverage gap cited in the brief.
- Testing `BackgroundRefreshController`, `TierBasedHydrationOrchestrator`, `BackgroundRefreshTaskRegistry` (concrete implementation), or `BackgroundRefreshSchedulerService` — out of scope; only `RunHydrationTierHandler` is targeted.
- Adding coverage for sibling untested handlers (`ForceRefreshTaskHandler`, `GetAllHistoryHandler`, `GetBackgroundRefreshTasksHandler`, `GetTaskHistoryHandler`, `GetTaskStatusHandler`) — noted as a related gap but not part of this task.
- Any production code change to `RunHydrationTierHandler.cs` itself, unless writing the tests surfaces an actual bug (e.g. a mismatched flag) — if that happens, fix the bug and note it, but do not otherwise refactor the handler.
- Integration/E2E tests — this is unit-test-only work.

## Open Questions
None.

## Status: COMPLETE
