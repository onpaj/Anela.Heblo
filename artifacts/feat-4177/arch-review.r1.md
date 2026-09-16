# Architecture Review: GetTaskStatusHandler coverage-gap tests

## Skip Design: true

This is a backend-only, test-only change with no new or changed UI components, screens, or visual design decisions. The designer phase should produce a minimal pass-through artifact noting there is nothing to design.

## Architectural Fit Assessment

This is a pure test-coverage addition targeting an existing, already-correct MediatR query handler (`GetTaskStatusHandler`). It fits squarely into the project's established Vertical Slice / MediatR pattern: one handler per `UseCases/{UseCaseName}` folder, tested by one corresponding test class in `backend/test/Anela.Heblo.Tests/Application/{Module}/`. The sibling handler in the same module, `GetBackgroundRefreshTasksHandler`, already has a fully-analogous test file (`GetBackgroundRefreshTasksHandlerTests.cs`) that exercises the identical registry-mocking pattern (`Mock<IBackgroundRefreshTaskRegistry>`), the same DTO mapping conventions, and the same `MakeTaskConfig`/`MakeExecutionLog` builder-helper idiom described in `docs/architecture/testing-strategy.md`'s "Test Structure Pattern." No new integration points, no new dependencies, no schema or contract changes. This is the lowest-risk category of change in the codebase.

One material difference from the sibling test class must be respected: `GetTaskStatusHandler`'s constructor takes only `IBackgroundRefreshTaskRegistry` (verified by reading `GetTaskStatusHandler.cs`) — there is no `ILogger<GetTaskStatusHandler>` parameter, unlike `GetBackgroundRefreshTasksHandler`. The `MakeSut()` helper in the new test file must not be copy-pasted with a logger mock it doesn't need.

## Proposed Architecture

### Component Overview

```
GetTaskStatusRequest (TaskId)
        |
        v
GetTaskStatusHandler.Handle
        |
        +-- IBackgroundRefreshTaskRegistry.GetRegisteredTasks() -> IReadOnlyList<RefreshTaskConfiguration>
        |         (lookup by TaskId; FirstOrDefault)
        |
        |-- [not found] --> GetTaskStatusResponse { Found = false }         <-- FR-1
        |
        +-- [found] --> IBackgroundRefreshTaskRegistry.GetLastExecution(taskId) -> RefreshTaskExecutionLog?
                  |
                  |-- [null]     --> RefreshTaskStatusDto { LastExecution = null }        <-- FR-2
                  +-- [non-null] --> RefreshTaskStatusDto { LastExecution = MapToDto(log) } <-- FR-3
                              |
                              v
                  GetTaskStatusResponse { Found = true, Status = dto }
```

No new components. The three branches above map 1:1 onto the three functional requirements in `spec.r1.md`, and onto the three `[Fact]` tests to be written.

### Key Design Decisions

#### Decision 1: Test file location and naming
**Options considered:** (a) add to an existing test file; (b) create a new dedicated test file for `GetTaskStatusHandler`.
**Chosen approach:** (b) — create `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`, sitting alongside `GetBackgroundRefreshTasksHandlerTests.cs` in the same directory.
**Rationale:** One test class per handler is the established convention in this codebase (confirmed by directory listing of `Application/BackgroundRefresh/`); there is no existing test file for this handler to extend.

#### Decision 2: Mocking strategy
**Options considered:** (a) hand-rolled fake implementing `IBackgroundRefreshTaskRegistry`; (b) `Moq`-based mock, matching the sibling test.
**Chosen approach:** (b) `Mock<IBackgroundRefreshTaskRegistry>`.
**Rationale:** `Moq` is the project-standard mocking library per `docs/architecture/testing-strategy.md`, and it's already used identically for this exact interface in `GetBackgroundRefreshTasksHandlerTests.cs` — reusing the pattern minimizes review friction and keeps the two test files easy to compare side by side.

#### Decision 3: No production code change
**Options considered:** (a) leave `GetTaskStatusHandler.cs` untouched; (b) refactor it while adding tests (e.g. extract the not-found short-circuit).
**Chosen approach:** (a) — tests only.
**Rationale:** Code inspection confirms the handler already correctly implements the `Found`/null-safety contract described in the issue. The issue is explicitly a coverage gap, not a functional bug. Per CLAUDE.md's "Surgical changes" rule, touch only what the task requires — do not refactor working, already-correct code as a side effect of adding tests.

## Implementation Guidance

### Directory / Module Structure
Create exactly one new file:
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`

No other files are created or modified.

### Interfaces and Contracts
No interface or contract changes. The test file consumes existing types as-is:
- `GetTaskStatusHandler`, `GetTaskStatusRequest`, `GetTaskStatusResponse` (`Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus`)
- `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto` (`Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`)
- `IBackgroundRefreshTaskRegistry`, `RefreshTaskConfiguration`, `RefreshTaskExecutionLog`, `RefreshTaskExecutionStatus` (`Anela.Heblo.Xcc.Services.BackgroundRefresh`)

`MakeSut()` signature for the new file:
```csharp
private static (GetTaskStatusHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry) MakeSut()
{
    var registry = new Mock<IBackgroundRefreshTaskRegistry>();
    var sut = new GetTaskStatusHandler(registry.Object);
    return (sut, registry);
}
```
(No logger mock — confirmed the constructor takes only the registry.)

### Data Flow
Covered fully in Component Overview above; each test sets up `GetRegisteredTasks()` (and, where relevant, `GetLastExecution(taskId)`) on the mocked registry, invokes `Handle`, and asserts on the returned `GetTaskStatusResponse`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Copy-pasting the sibling `MakeSut()` including its `ILogger` mock, causing a compile error | Low | Explicitly called out in Decision 1 / Interfaces section above; verify against `GetTaskStatusHandler.cs`'s actual constructor before writing the helper. |
| FR-1's "GetLastExecution never called" assertion becomes brittle if written as a strict `Verify` | Low | Spec already phrases this as acceptable via either an explicit `Verify(x => x.GetLastExecution(...), Times.Never())` or simply leaving the setup absent (Moq's loose mode returns default and won't throw) — either is fine; prefer `Times.Never()` for an explicit, self-documenting assertion. |
| None of these tests exercise the MediatR pipeline (validation behaviors, etc.) | Low | Out of scope per spec — this is unit-level handler testing, consistent with "70% unit tests" test-pyramid guidance and the existing sibling test file's scope. |

## Specification Amendments
None. The spec is implementable as written. One clarifying note carried into implementation guidance above: `GetTaskStatusHandler`'s constructor has no `ILogger` parameter (spec's "API / Interface Design" section already flagged this as something to confirm — confirmed here).

## Prerequisites
None. No migrations, config, or infrastructure changes are required. The existing `Anela.Heblo.Tests` project already references `Moq` and `FluentAssertions`.
