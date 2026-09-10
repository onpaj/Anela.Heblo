# Architecture Review: Unit test coverage for GetBackgroundRefreshTasksHandler.MapToDto

## Skip Design: true

This is a backend-only, test-only coverage-gap fix. There are no new or changed UI components, screens, or visual design decisions — the designer phase should produce a no-op/skip artifact.

## Architectural Fit Assessment

The spec is a pure test-addition task and fits the codebase's existing conventions exactly. `docs/architecture/testing-strategy.md` mandates xUnit + Moq + FluentAssertions with a `HandlerTests` class per MediatR handler, one mock per dependency, constructed in a per-test (or per-fixture) setup. `GetBackgroundRefreshTasksHandler` sits in the same feature slice (`Features/BackgroundRefresh/UseCases/`) as `RunHydrationTierHandler`, whose test (`RunHydrationTierHandlerTests.cs`, in `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/`) already establishes the exact local convention to mirror: a `MakeSut()` triple-tuple helper, a `MakeTaskConfig(...)` fixture builder, `Mock<IBackgroundRefreshTaskRegistry>` + `Mock<ILogger<T>>`, FluentAssertions on the response. No new abstractions, interfaces, or structural changes are needed — the only integration point is the existing `IBackgroundRefreshTaskRegistry` mock surface (`GetRegisteredTasks()`, `GetLastExecution(string)`), which the handler already depends on and which is fully mockable via the interface. There is no risk of touching module boundaries (Application → Xcc dependency already exists and is one-directional; tests only consume it).

## Proposed Architecture

### Component Overview

```
GetBackgroundRefreshTasksHandlerTests (NEW)
        |
        | constructs, mocks IBackgroundRefreshTaskRegistry + ILogger<T>
        v
GetBackgroundRefreshTasksHandler.Handle(request, ct)   [UNCHANGED]
        |
        | GetRegisteredTasks() -> IReadOnlyList<RefreshTaskConfiguration>
        | GetLastExecution(taskId) -> RefreshTaskExecutionLog?
        v
MapToDto(task, lastExecution) -> RefreshTaskDto        [private, exercised indirectly]
        |
        +-- NextScheduledRun: task.Enabled && lastExecution?.CompletedAt != null
        +-- LastExecution: lastExecution != null ? MapToExecutionLogDto(...) : null
```

No new components are introduced. The test class is a pure consumer of the existing public `Handle` entry point; `MapToDto` and `MapToExecutionLogDto` stay private and are only exercised indirectly through `Handle`'s return value, exactly as the spec requires (NFR-1: no production code changes).

### Key Design Decisions

#### Decision 1: Test through `Handle`, not by making `MapToDto` internal/testable directly
**Options considered:**
- (a) Exercise `MapToDto` only indirectly via `Handle`, asserting on `GetBackgroundRefreshTasksResponse.Tasks`.
- (b) Make `MapToDto` `internal` + `[InternalsVisibleTo]` the test project, or `internal static` with direct unit tests.

**Chosen approach:** (a) — test only through `Handle`.

**Rationale:** The spec explicitly scopes this as test-only with no production code changes (NFR-1), and the issue itself frames the requirement as "Unit tests for the `Handle` method." `Handle` with a single registered task is a thin, deterministic pass-through to `MapToDto`, so testing through the public seam gives full branch coverage of the private method with zero API surface change and zero risk of accidentally coupling tests to `MapToDto`'s signature. This also matches the sibling `RunHydrationTierHandlerTests.cs` convention of testing exclusively through `Handle`.

#### Decision 2: One task per registry setup for branch-coverage tests, plus one multi-task test
**Options considered:**
- (a) Every `[Fact]` registers exactly one task and asserts on `Tasks.Single()`.
- (b) Table-driven `[Theory]`/`[InlineData]` covering all four `NextScheduledRun` combinations in one test method.

**Chosen approach:** Hybrid — single-task `[Fact]`s for each of the branch-coverage cases in spec FR-2/FR-3 (mirrors the existing sibling test's `[Fact]`-per-scenario style, and keeps each test's Arrange block trivial to read), plus one dedicated `[Fact]` for the multi-task case in FR-5. Do **not** introduce `[Theory]`/`[InlineData]` for this — `RefreshTaskConfiguration`/`RefreshTaskExecutionLog` require multiple related field values per case (task enabled state, `CompletedAt`, `RefreshInterval`) that don't collapse cleanly into `InlineData`'s primitive-parameter shape without a custom test-data class, and the sibling test in this same directory does not use `[Theory]` either — consistency with the local convention outweighs the deduplication `[Theory]` would buy for 4-5 cases.

**Rationale:** Matches `RunHydrationTierHandlerTests.cs` exactly (per-scenario `[Fact]`s, no `[Theory]`), keeps diff minimal and reviewable, and each test name documents one behavior directly (readable in CI failure output without needing to expand parameterized case data).

#### Decision 3: Fixed literal `DateTime` values, not `DateTime.UtcNow`
**Options considered:**
- (a) Use `DateTime.UtcNow`-relative values with tolerance-based assertions (e.g., `.Should().BeCloseTo(...)`).
- (b) Use fixed literal `DateTime` fixtures (e.g., `new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)`) with exact-value assertions.

**Chosen approach:** (b) — fixed literals, exact assertions, per spec NFR-2.

**Rationale:** `NextScheduledRun` is a pure arithmetic computation (`CompletedAt + RefreshInterval`) with no clock dependency in the handler itself (the handler injects no `TimeProvider`/`IClock`) — there is no reason to introduce tolerance and every reason not to: exact literal values make the FR-2 case-4 assertion ("equals `CompletedAt + RefreshInterval`") a precise, deterministic check rather than an approximate one, and eliminate any flakiness risk entirely.

## Implementation Guidance

### Directory / Module Structure

Single new file, no changes elsewhere:

```
backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/
  GetBackgroundRefreshTasksHandlerTests.cs   <- NEW (only file added/changed)
```

Namespace: `Anela.Heblo.Tests.Application.BackgroundRefresh` (matches the existing sibling file in the same directory).

### Interfaces and Contracts

No new or changed interfaces/contracts. The test class depends only on already-public/internal types the handler itself uses:
- `IBackgroundRefreshTaskRegistry` (mocked) — `GetRegisteredTasks()`, `GetLastExecution(string)`.
- `RefreshTaskConfiguration` — construct via object initializer (`required` members: `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`; `HydrationTier` defaults to `1`).
- `RefreshTaskExecutionLog` (a `record`) — construct via object initializer (`required` members: `TaskId`, `StartedAt`, `Status`; `CompletedAt`/`ErrorMessage`/`Metadata` optional; `Duration` is computed, do not set).
- `GetBackgroundRefreshTasksRequest` / `GetBackgroundRefreshTasksResponse` / `RefreshTaskDto` / `RefreshTaskExecutionLogDto` — assert against, unmodified.
- `ILogger<GetBackgroundRefreshTasksHandler>` (mocked, unused for assertions — `Handle` does not log; include only to satisfy the constructor, matching the sibling pattern which mocks the logger even when a given test doesn't assert on it).

Suggested test helpers (private, in the new test class — not shared/extracted, since only this one test class needs them):
```csharp
private static (GetBackgroundRefreshTasksHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry) MakeSut()

private static RefreshTaskConfiguration MakeTaskConfig(
    string taskId = "task-a", bool enabled = true, TimeSpan? refreshInterval = null, int hydrationTier = 1)

private static RefreshTaskExecutionLog MakeExecutionLog(
    string taskId = "task-a", DateTime? startedAt = null, DateTime? completedAt = null,
    RefreshTaskExecutionStatus status = RefreshTaskExecutionStatus.Completed)
```
(`MakeSut()` here omits the logger mock from its return tuple since no test needs to assert on log calls — simpler than the sibling's triple return; still construct and pass a `Mock<ILogger<...>>().Object` to the handler's constructor internally.)

### Data Flow

For every test: `MakeSut()` → `registry.Setup(r => r.GetRegisteredTasks()).Returns([...])` → `registry.Setup(r => r.GetLastExecution(It.IsAny<string>())).Returns(...)` (or per-`taskId` `It.Is<string>(...)` setups for the multi-task test in FR-5, so each task's `GetLastExecution` call returns its own distinct log/`null`) → `await sut.Handle(new GetBackgroundRefreshTasksRequest(), default)` → assert on `response.Tasks`.

For the FR-5 multi-task test, set up `GetLastExecution` with per-task-id matching (`registry.Setup(r => r.GetLastExecution("task-a")).Returns(logA); registry.Setup(r => r.GetLastExecution("task-b")).Returns((RefreshTaskExecutionLog?)null);`) rather than a single `It.IsAny<string>()` catch-all, so each task's independent mapping is verifiably tied to its own inputs.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `RefreshTaskExecutionLog` is a `record` with a computed `Duration` property (`CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null`) — asserting `LastExecution.Duration` requires the test's own expected-value math to match this exactly | Low | When asserting `Duration` in FR-3, compute the expected value the same way (`completedAt - startedAt`) rather than hardcoding a literal, so the test doesn't silently rely on coincidental arithmetic |
| `Status` on `RefreshTaskExecutionLogDto` is `.ToString()` of the `RefreshTaskExecutionStatus` enum — a test could assert against the wrong casing/format | Low | Assert `LastExecution.Status` against `RefreshTaskExecutionStatus.Completed.ToString()` (or the specific enum value used in that test's fixture), not a hardcoded string literal, so it stays correct if the enum is ever renamed |
| Coverage-gap-only tests can drift into testing implementation details rather than behavior, if not scoped carefully | Low | FR-1..FR-5 in the spec already scope exactly which behaviors to assert (four `NextScheduledRun` combinations, `LastExecution` null/non-null, pass-through fields, multi-task independence) — the developer should not add tests beyond this scope for a coverage-gap issue |

## Specification Amendments

None. The spec (`spec.r1.md`) is implementable as written; this review only adds structural/naming guidance (helper signatures, per-task-id mock setup for FR-5) that a developer would otherwise have had to decide independently — nothing here changes the spec's functional requirements or acceptance criteria.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed — the test project, xUnit/Moq/FluentAssertions packages, and the target handler all already exist and are already referenced by `Anela.Heblo.Tests`.
