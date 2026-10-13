# Specification: GetTaskStatusHandler coverage-gap tests

## Summary
`GetTaskStatusHandler` (BackgroundRefresh / GetTaskStatus use case) has two branches with no unit test coverage: the "task not registered" branch and the "task registered but never executed" branch. This spec defines the unit tests needed to close that coverage gap and lock down the `Found` contract that callers rely on.

## Background
`GetTaskStatusHandler.Handle` looks up a task by `TaskId` in `IBackgroundRefreshTaskRegistry.GetRegisteredTasks()`. If no matching task is found, it returns `GetTaskStatusResponse { Found = false }` with `Status` left null. If a task is found, it calls `GetLastExecution(taskId)`, which may return null when the task has never run, and maps the result into a `RefreshTaskStatusDto` whose `LastExecution` is null-safe-mapped. Current test coverage for this handler is 14.3% (CI run #34699120372, filed 2026-09-14), because no test exists in `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/` for this handler — only the sibling `GetBackgroundRefreshTasksHandlerTests.cs` exists, covering a related but different handler. Callers (the background-refresh status page) depend on `Found=false` to distinguish "not registered" from "registered but idle"; a silent regression (e.g. `Found` always `true`, or a NullReferenceException on unexecuted tasks) would not be caught by any existing test today.

## Functional Requirements

### FR-1: Task not found in registry
When `request.TaskId` does not match any task returned by `_taskRegistry.GetRegisteredTasks()`, the handler must return a response with `Found = false` and `Status = null`, and must not call `_taskRegistry.GetLastExecution(...)`.

**Acceptance criteria:**
- Given an empty (or non-matching) registered-tasks list, when `Handle` is invoked with any `TaskId`, then `response.Found` is `false`.
- `response.Status` is `null`.
- The handler completes without throwing.
- `GetLastExecution` is never invoked for a task that isn't registered (registry mock verifies no call, or is simply not set up so a call would throw `MockException`/return default — test should assert `Found=false` regardless of `GetLastExecution` setup).

### FR-2: Task registered, no last execution recorded
When the requested task is present in `GetRegisteredTasks()` but `_taskRegistry.GetLastExecution(taskId)` returns `null`, the handler must return `Found = true`, a non-null `Status`, with `Status.LastExecution = null`, and must not throw a `NullReferenceException`.

**Acceptance criteria:**
- Given a registered task and `GetLastExecution` returning `null`, when `Handle` is invoked, then `response.Found` is `true`.
- `response.Status` is not null.
- `response.Status.LastExecution` is `null`.
- `response.Status.TaskId`, `Enabled`, and `RefreshInterval` are mapped from the registered task's configuration (pass-through fields), matching the pattern already exercised for the sibling handler in `GetBackgroundRefreshTasksHandlerTests.cs`.

### FR-3: Happy path — task registered with a last execution
When the requested task is present and `GetLastExecution(taskId)` returns a non-null `RefreshTaskExecutionLog`, the handler must return `Found = true` and `Status.LastExecution` populated with every field mapped from the log (`TaskId`, `StartedAt`, `CompletedAt`, `Status` (as string), `ErrorMessage`, `Duration`, `Metadata`).

**Acceptance criteria:**
- Given a registered task and a populated `RefreshTaskExecutionLog`, when `Handle` is invoked, then `response.Found` is `true` and `response.Status.LastExecution` is not null.
- Each `RefreshTaskExecutionLogDto` field equals the corresponding source `RefreshTaskExecutionLog` field, with `Status` converted via `.ToString()`.
- This test acts as a regression guard for the existing `MapToDto` mapping, and as a baseline contrasted against FR-1/FR-2's negative-path assertions.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a pure unit-test coverage change; no production code path changes are anticipated (see Open Questions for the one case where a production change might become necessary).

### NFR-2: Security
Not applicable — no auth, PII, or data-sensitivity concerns; the handler consumes only registry data supplied by test doubles.

## Data Model
No new data model. Existing types touched by these tests:
- `GetTaskStatusRequest { string TaskId }`
- `GetTaskStatusResponse : BaseResponse { bool Found; RefreshTaskStatusDto? Status }`
- `RefreshTaskStatusDto` (in `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`) — `TaskId`, `Enabled`, `RefreshInterval`, `LastExecution`
- `RefreshTaskExecutionLogDto` — `TaskId`, `StartedAt`, `CompletedAt`, `Status` (string), `ErrorMessage`, `Duration`, `Metadata`
- `RefreshTaskConfiguration` (registry-side) — `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier`
- `RefreshTaskExecutionLog` (registry-side) — `TaskId`, `StartedAt`, `CompletedAt`, `Status` (enum `RefreshTaskExecutionStatus`), `ErrorMessage`, `Duration`, `Metadata`

## API / Interface Design
No interface or API changes. Tests are added to a new file:
`backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`

Mirroring the existing sibling test's structure (`GetBackgroundRefreshTasksHandlerTests.cs`):
- A `MakeSut()` helper constructing `GetTaskStatusHandler` with a `Mock<IBackgroundRefreshTaskRegistry>`. Note: unlike the sibling handler, `GetTaskStatusHandler`'s constructor takes only `IBackgroundRefreshTaskRegistry` — no `ILogger` parameter — confirm this when wiring the mock helper.
- Reusable `MakeTaskConfig(...)` and `MakeExecutionLog(...)` builder helpers (can be copied/adapted from the sibling test file).
- Three `[Fact]` tests, one per functional requirement above (FR-1, FR-2, FR-3).

## Dependencies
- `Moq` and `FluentAssertions` (already used throughout `Anela.Heblo.Tests`, see sibling test file).
- No new package dependencies.
- Depends on existing `IBackgroundRefreshTaskRegistry`, `RefreshTaskConfiguration`, `RefreshTaskExecutionLog` types in `Anela.Heblo.Xcc.Services.BackgroundRefresh`.

## Out of Scope
- No production code changes to `GetTaskStatusHandler.cs` are planned — the branches already implement the documented contract correctly per code inspection; this work is purely test coverage.
- No changes to the background-refresh status page (frontend) that consumes this endpoint.
- No changes to `GetBackgroundRefreshTasksHandler` or its existing tests.
- No integration/E2E tests — this is a unit-test-only coverage gap per the issue's "Suggested approach."

## Open Questions
None.
