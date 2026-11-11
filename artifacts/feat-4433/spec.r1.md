# Specification: ForceRefreshTaskHandler unit tests (NotFound and unexpected-error paths)

## Summary
Add a unit test class for `ForceRefreshTaskHandler` covering all three return paths (success, task not found, unexpected error). Test-only change; no production code changes. Raises line coverage of the handler from 26% to above the 60% threshold.

## Background
Issue #4433 (coverage-gap routine, CI run #36876655065). `Handle` calls `IBackgroundRefreshTaskRegistry.ForceRefreshAsync` and maps outcomes: no exception -> `Success=true`; `InvalidOperationException` -> `NotFound=true`, `ErrorMessage=ex.Message`; any other exception -> `Success=false`, fixed generic `ErrorMessage`. The frontend uses the response shape to pick what to show, so regressions in the mapping would be silent. No `ForceRefreshTaskHandlerTests` exist; sibling tests live in `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/` (e.g. `RunHydrationTierHandlerTests.cs`).

## Functional Requirements

### FR-1: Success path
**Acceptance criteria:**
- Registry mock completes `ForceRefreshAsync(taskId, ct)` normally; response has `Success == true`, `NotFound == false`, `ErrorMessage == null`.
- Registry `ForceRefreshAsync` is called exactly once with the request `TaskId` and the supplied cancellation token.

### FR-2: NotFound path
**Acceptance criteria:**
- Registry throws `InvalidOperationException("Task with ID 'x' is not registered")`; response has `NotFound == true` and `ErrorMessage` equal to the exception message.
- A Warning-level log is emitted; no Error-level log.

### FR-3: Unexpected-error path
**Acceptance criteria:**
- Registry throws a non-`InvalidOperationException` (e.g. `Exception`/`TimeoutException`); response has `Success == false`, `NotFound == false`, `ErrorMessage == "An unexpected error occurred during force refresh"`; the original exception text is not leaked.
- An Error-level log is emitted.

### FR-4: Exception-type precedence
**Acceptance criteria:**
- A subclass of `InvalidOperationException` (e.g. `ObjectDisposedException`) is treated as NotFound, documenting the catch ordering.

## Non-Functional Requirements
- NFR-1 Performance: pure in-memory mocks, each test well under 100 ms.
- NFR-2 Security: none; no secrets or I/O.
- NFR-3 Style: xUnit + Moq + FluentAssertions, matching `RunHydrationTierHandlerTests`.

## Data Model
None.

## API / Interface Design
No change. Types under test: `ForceRefreshTaskRequest { required string TaskId }`, `ForceRefreshTaskResponse : BaseResponse { bool NotFound; string? ErrorMessage }`.

## Dependencies
Moq, FluentAssertions, xUnit (already referenced by `Anela.Heblo.Tests`).

## Out of Scope
- Changing handler behavior.
- Frontend/controller tests.
- Process docs: no sync, calculation or job behavior changes, so no `docs/processes/` update is needed.

## Open Questions
- Observation, not to be fixed here: in the NotFound branch `Success` stays at the `BaseResponse` default of `true`, because only `NotFound` and `ErrorMessage` are set. This contradicts the issue's implied "structurally different" contract for callers that check `Success` only. Assumption: tests assert current behavior for `NotFound`/`ErrorMessage` and do NOT assert `Success` on the NotFound path; a follow-up issue may decide whether to set `Success=false`.

## Status: COMPLETE
