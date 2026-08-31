# Specification: Unit test coverage for GetBackgroundRefreshTasksHandler.MapToDto

## Summary
`GetBackgroundRefreshTasksHandler` maps registered background-refresh tasks and their last execution log into `RefreshTaskDto` objects for the background-tasks dashboard. Its private `MapToDto` method contains two conditional branches — a compound condition computing `NextScheduledRun` and a null-check populating `LastExecution` — that are almost entirely untested (12.5% line coverage against a 60% threshold). This spec covers adding unit tests for the `Handle` method that exercise every branch of `MapToDto` via a mocked `IBackgroundRefreshTaskRegistry`, with no production code changes.

## Background
`GetBackgroundRefreshTasksHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksHandler.cs`) is a MediatR request handler invoked by `BackgroundRefreshController` to list all registered background-refresh tasks with their scheduling state, for the background-tasks dashboard UI. It reads registered tasks and each task's last execution log from `IBackgroundRefreshTaskRegistry`, then maps them via the private `MapToDto` method.

`MapToDto` computes `NextScheduledRun` only when `task.Enabled == true` AND `lastExecution?.CompletedAt != null`, and maps `LastExecution` only when `lastExecution != null`. These branches are currently exercised by no unit test in the repository (`backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/` has no test file for this handler at all — it contains only `RunHydrationTierHandlerTests.cs` for a sibling handler in the same feature slice). A regression in either branch would be invisible until a user notices wrong or missing "next run" / "last execution" timestamps on the dashboard, since this is a pure mapping layer with no other safety net (no integration test, no UI assertion tying dashboard values back to source state).

This is a coverage-gap-only change: add tests, do not modify `GetBackgroundRefreshTasksHandler.cs` or any DTO/contract it depends on.

## Functional Requirements

### FR-1: Test file and structure
Add `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs`, following the existing sibling test's conventions (`RunHydrationTierHandlerTests.cs` in the same directory/namespace):
- Namespace `Anela.Heblo.Tests.Application.BackgroundRefresh`.
- xUnit `[Fact]` tests, `Moq` for `Mock<IBackgroundRefreshTaskRegistry>` and `Mock<ILogger<GetBackgroundRefreshTasksHandler>>`, `FluentAssertions` for assertions.
- A private `MakeSut()` helper constructing `(GetBackgroundRefreshTasksHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry, Mock<ILogger<GetBackgroundRefreshTasksHandler>> Logger)`, mirroring the sibling test's pattern.
- A private `MakeTaskConfig(...)` (or equivalent) helper building `RefreshTaskConfiguration` instances with sensible defaults, parameterizable at least by `enabled`.

**Acceptance criteria:**
- File compiles and follows the existing project test conventions (xUnit/Moq/FluentAssertions, `Mock<T>` construction pattern matching `RunHydrationTierHandlerTests.cs`).
- Tests call `sut.Handle(new GetBackgroundRefreshTasksRequest(), default)` and assert on the returned `GetBackgroundRefreshTasksResponse.Tasks` (single-task setups may assert `Tasks.Single()` or `Tasks[0]`).

### FR-2: NextScheduledRun compound condition coverage
Cover all four combinations of the compound condition `task.Enabled && lastExecution?.CompletedAt != null` used to compute `NextScheduledRun`, per the issue's suggested approach:

1. **Disabled task, with a completed last execution** → `NextScheduledRun` is `null` (disabled always wins regardless of `lastExecution`).
2. **Enabled task, no `lastExecution` at all** (registry returns `null` for `GetLastExecution`) → `NextScheduledRun` is `null`; `LastExecution` is also `null` (this case double-covers FR-3's null branch and may be shared with it).
3. **Enabled task, `lastExecution` present but `lastExecution.CompletedAt` is `null`** (e.g. status `Running`, task still in flight) → `NextScheduledRun` is `null`.
4. **Enabled task, `lastExecution.CompletedAt` set** → `NextScheduledRun` equals `lastExecution.CompletedAt.Value.Add(task.RefreshInterval)` exactly (assert the precise computed `DateTime`, not just non-null).

**Acceptance criteria:**
- Each of the four combinations above is asserted in its own `[Fact]` (or clearly separated test cases), each asserting `NextScheduledRun` on the single mapped task in the response.
- Case 4 asserts the exact expected value (`CompletedAt + RefreshInterval`), not merely `.Should().NotBeNull()`.
- Test data uses distinct, deterministic `DateTime`/`TimeSpan` values (not `DateTime.Now`/`UtcNow`) so the expected value is computable and stable.

### FR-3: LastExecution mapping coverage
Cover both branches of `lastExecution != null ? MapToExecutionLogDto(lastExecution) : null`:

1. **No `lastExecution`** → `LastExecution` on the resulting DTO is `null` (may be covered by FR-2 case 2).
2. **`lastExecution` present** → `LastExecution` is non-null and its fields are correctly mapped from the source `RefreshTaskExecutionLog`: `TaskId`, `StartedAt`, `CompletedAt`, `Status` (mapped via `.ToString()` from the `RefreshTaskExecutionStatus` enum), `ErrorMessage`, `Duration`, `Metadata`.

**Acceptance criteria:**
- At least one test asserts `LastExecution` is `null` when the registry returns no execution log.
- At least one test asserts `LastExecution` is non-null with each of its fields matching the corresponding field on the source `RefreshTaskExecutionLog` used in the mock setup.

### FR-4: Pass-through field coverage (supporting, not primary)
While mapping the always-present fields (`TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier`) is not the focus of this issue, at least one test should assert these are passed through correctly from `RefreshTaskConfiguration` to `RefreshTaskDto`, since they are exercised incidentally by the tests above and provide cheap additional coverage.

**Acceptance criteria:**
- At least one test asserts `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, and `HydrationTier` on the mapped DTO match the source `RefreshTaskConfiguration`.

### FR-5: Multiple tasks (Handle-level behavior)
Add at least one test with multiple registered tasks (mixing enabled/disabled and with/without last execution) asserting `Handle` maps each task independently and preserves order/count, since `Handle` iterates `GetRegisteredTasks()` via `.Select(...).ToList()`.

**Acceptance criteria:**
- A test registers 2+ tasks with different `Enabled`/`lastExecution` combinations and asserts `response.Tasks.Count` and that each task's `NextScheduledRun`/`LastExecution` independently reflects its own inputs (not cross-contaminated).

## Non-Functional Requirements

### NFR-1: No production code changes
This is a test-only change. `GetBackgroundRefreshTasksHandler.cs` and all DTOs/contracts under `Contracts/` must remain unmodified.

### NFR-2: Determinism
No test may depend on wall-clock time (`DateTime.Now`/`UtcNow`) for correctness assertions — all `DateTime` values used in expected-value assertions must be literal/fixed test fixtures.

### NFR-3: Isolation
Tests must not depend on execution order or shared mutable state between `[Fact]`s — each test constructs its own `MakeSut()` instance and its own mock setups.

## Data Model
No data model changes. Relevant existing types (all unmodified):
- `RefreshTaskConfiguration` (`Anela.Heblo.Xcc.Services.BackgroundRefresh`): `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier` (all required except `HydrationTier`, default `1`).
- `RefreshTaskExecutionLog` (`Anela.Heblo.Xcc.Services.BackgroundRefresh`, a `record`): `TaskId`, `StartedAt`, `CompletedAt` (nullable), `Status` (`RefreshTaskExecutionStatus` enum: `Running`, `Completed`, `Failed`, `Cancelled`), `ErrorMessage`, `Duration` (computed, nullable), `Metadata`.
- `RefreshTaskDto` / `RefreshTaskExecutionLogDto` (`Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`): the mapping targets under test.
- `IBackgroundRefreshTaskRegistry`: mocked interface exposing `GetRegisteredTasks()` and `GetLastExecution(string taskId)`, both used by `Handle`.

## API / Interface Design
No API changes. Tests exercise `GetBackgroundRefreshTasksHandler.Handle(GetBackgroundRefreshTasksRequest, CancellationToken)` directly (unit-level, no HTTP/controller involvement).

## Dependencies
- Existing test project `Anela.Heblo.Tests` and its already-referenced packages: xUnit, Moq, FluentAssertions.
- No new NuGet packages required.

## Out of Scope
- Changes to `GetBackgroundRefreshTasksHandler.cs` production logic.
- Testing `BackgroundRefreshController` or any HTTP-level behavior.
- Testing `BackgroundRefreshTaskRegistry` itself (already covered by `BackgroundRefreshTaskRegistryTests.cs`).
- Frontend (`useBackgroundRefresh.ts`) changes or tests.
- Testing `GetExecutionHistory` or `ForceRefreshAsync` (not used by this handler).

## Open Questions

None.

## Status: COMPLETE
