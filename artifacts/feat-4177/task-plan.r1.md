# GetTaskStatusHandler Coverage-Gap Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit test coverage for `GetTaskStatusHandler`'s two currently-untested branches (task not registered, task registered but never executed), plus a happy-path regression test, closing the coverage gap identified in issue #4177.

**Architecture:** Pure test-only addition. One new xUnit test class, `GetTaskStatusHandlerTests`, mirrors the existing sibling `GetBackgroundRefreshTasksHandlerTests` pattern in the same directory: a `MakeSut()` helper wiring a `Mock<IBackgroundRefreshTaskRegistry>` into `GetTaskStatusHandler` (no logger — this handler's constructor takes only the registry), plus `MakeTaskConfig`/`MakeExecutionLog` builder helpers. Three `[Fact]` tests cover FR-1 (not found), FR-2 (found, no last execution), and FR-3 (found, with last execution), one TDD cycle each. No production code changes.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions.

---

### task: setup-test-file

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`

This task creates the test file skeleton with the `MakeSut()`, `MakeTaskConfig()`, and `MakeExecutionLog()` helpers, with no test methods yet, and confirms it compiles as part of the existing test project (no new project references needed — `Moq` and `FluentAssertions` are already referenced by `Anela.Heblo.Tests.csproj`, as used by the sibling `GetBackgroundRefreshTasksHandlerTests.cs` in the same directory).

- [ ] **Step 1: Create the test file with helpers only**

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.BackgroundRefresh;

public class GetTaskStatusHandlerTests
{
    private static (GetTaskStatusHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry) MakeSut()
    {
        var registry = new Mock<IBackgroundRefreshTaskRegistry>();
        var sut = new GetTaskStatusHandler(registry.Object);
        return (sut, registry);
    }

    private static RefreshTaskConfiguration MakeTaskConfig(
        string taskId = "task-a",
        bool enabled = true,
        TimeSpan? refreshInterval = null,
        int hydrationTier = 1) =>
        new()
        {
            TaskId = taskId,
            InitialDelay = TimeSpan.FromMinutes(1),
            RefreshInterval = refreshInterval ?? TimeSpan.FromHours(1),
            Enabled = enabled,
            HydrationTier = hydrationTier,
        };

    private static RefreshTaskExecutionLog MakeExecutionLog(
        string taskId = "task-a",
        DateTime? startedAt = null,
        DateTime? completedAt = null,
        RefreshTaskExecutionStatus status = RefreshTaskExecutionStatus.Completed,
        string? errorMessage = null,
        Dictionary<string, object>? metadata = null) =>
        new()
        {
            TaskId = taskId,
            StartedAt = startedAt ?? new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            CompletedAt = completedAt,
            Status = status,
            ErrorMessage = errorMessage,
            Metadata = metadata,
        };
}
```

- [ ] **Step 2: Build to verify the skeleton compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Build succeeded.` — no errors. (No tests exist yet, so there is nothing to run.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs
git commit -m "test: add GetTaskStatusHandlerTests skeleton with builder helpers"
```

---

### task: fr1-not-found-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`

Implements FR-1: when `TaskId` matches no registered task, `Handle` returns `Found = false`, `Status = null`, without throwing, and without calling `GetLastExecution`.

- [ ] **Step 1: Write the failing test**

Add this method inside the `GetTaskStatusHandlerTests` class (after the helpers):

```csharp
    // FR-1: task not present in the registry returns Found = false, Status = null,
    // and never calls GetLastExecution.
    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTaskIdIsNotRegistered()
    {
        var (sut, registry) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig(taskId: "other-task"),
        });

        var response = await sut.Handle(new GetTaskStatusRequest { TaskId = "missing-task" }, default);

        response.Found.Should().BeFalse();
        response.Status.Should().BeNull();
        registry.Verify(r => r.GetLastExecution(It.IsAny<string>()), Times.Never());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTaskStatusHandlerTests"`
Expected: FAIL — compile error is not expected since `GetTaskStatusHandler.Handle` already implements this branch correctly (per `arch-review.r1.md`, no production code changes are needed); if the test fails to compile, check that `GetTaskStatusRequest`/`GetTaskStatusHandler` namespaces above match the `using` statements from Step 1 of `setup-test-file`. If it compiles, this specific test is expected to already PASS at this point, because the production code correctly implements FR-1 — this is a coverage-only change, not a bug fix. Confirm the test passes here rather than expecting a red step; the TDD value in this task is confirming the test actually exercises and pins the existing correct behavior.

- [ ] **Step 3: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_ReturnsNotFound_WhenTaskIdIsNotRegistered"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs
git commit -m "test: cover GetTaskStatusHandler not-found branch (FR-1)"
```

---

### task: fr2-no-last-execution-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`

Implements FR-2: when the task is registered but `GetLastExecution` returns `null`, `Handle` returns `Found = true`, a non-null `Status` with `LastExecution = null`, and pass-through fields (`TaskId`, `Enabled`, `RefreshInterval`) mapped from the registered configuration.

- [ ] **Step 1: Write the failing test**

Add this method after `Handle_ReturnsNotFound_WhenTaskIdIsNotRegistered`:

```csharp
    // FR-2: task registered but never executed returns Found = true, Status.LastExecution = null,
    // with pass-through fields mapped from the registered configuration.
    [Fact]
    public async Task Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted()
    {
        var (sut, registry) = MakeSut();
        var refreshInterval = TimeSpan.FromMinutes(30);
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig(taskId: "task-a", enabled: true, refreshInterval: refreshInterval),
        });
        registry.Setup(r => r.GetLastExecution("task-a")).Returns((RefreshTaskExecutionLog?)null);

        var response = await sut.Handle(new GetTaskStatusRequest { TaskId = "task-a" }, default);

        response.Found.Should().BeTrue();
        response.Status.Should().NotBeNull();
        response.Status!.LastExecution.Should().BeNull();
        response.Status.TaskId.Should().Be("task-a");
        response.Status.Enabled.Should().BeTrue();
        response.Status.RefreshInterval.Should().Be(refreshInterval);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted"`
Expected: as with FR-1, the production code already implements this correctly, so this test is expected to compile and PASS immediately — there is no bug to fix. Verify it fails only if the `Status`/`RefreshTaskStatusDto` property names above don't match `RefreshTaskStatusDto` in `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs` (they should: `TaskId`, `Enabled`, `RefreshInterval`, `LastExecution`).

- [ ] **Step 3: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs
git commit -m "test: cover GetTaskStatusHandler never-executed branch (FR-2)"
```

---

### task: fr3-happy-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`

Implements FR-3: when the task is registered and `GetLastExecution` returns a populated `RefreshTaskExecutionLog`, `Handle` returns `Found = true` and `Status.LastExecution` with every field mapped from the log, including `Status` converted via `.ToString()` and the computed `Duration`.

- [ ] **Step 1: Write the failing test**

Add this method after `Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted`:

```csharp
    // FR-3: happy path -- task registered with a last execution maps every LastExecution field
    // from the source log, including Status.ToString() and the computed Duration.
    [Fact]
    public async Task Handle_ReturnsFoundWithMappedLastExecution_WhenTaskHasExecuted()
    {
        var (sut, registry) = MakeSut();
        var startedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var completedAt = new DateTime(2026, 1, 1, 9, 5, 0, DateTimeKind.Utc);
        var metadata = new Dictionary<string, object> { ["rows"] = 42 };
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig(taskId: "task-a", enabled: true),
        });
        registry.Setup(r => r.GetLastExecution("task-a")).Returns(MakeExecutionLog(
            taskId: "task-a",
            startedAt: startedAt,
            completedAt: completedAt,
            status: RefreshTaskExecutionStatus.Failed,
            errorMessage: "boom",
            metadata: metadata));

        var response = await sut.Handle(new GetTaskStatusRequest { TaskId = "task-a" }, default);

        response.Found.Should().BeTrue();
        response.Status.Should().NotBeNull();
        var lastExecution = response.Status!.LastExecution;
        lastExecution.Should().NotBeNull();
        lastExecution!.TaskId.Should().Be("task-a");
        lastExecution.StartedAt.Should().Be(startedAt);
        lastExecution.CompletedAt.Should().Be(completedAt);
        lastExecution.Status.Should().Be(RefreshTaskExecutionStatus.Failed.ToString());
        lastExecution.ErrorMessage.Should().Be("boom");
        lastExecution.Duration.Should().Be(completedAt - startedAt);
        lastExecution.Metadata.Should().BeEquivalentTo(metadata);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_ReturnsFoundWithMappedLastExecution_WhenTaskHasExecuted"`
Expected: as with FR-1/FR-2, `GetTaskStatusHandler.MapToDto` already maps every field correctly, so this test is expected to compile and PASS immediately. This test acts as a regression guard, pinning the existing correct `MapToDto` behavior — confirm it fails only if there's a typo in a field/property name relative to `RefreshTaskExecutionLogDto` (`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs`) or `RefreshTaskExecutionLog` (`backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskExecutionLog.cs`, where `Duration` is a computed property `CompletedAt.Value - StartedAt`, not a settable field).

- [ ] **Step 3: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_ReturnsFoundWithMappedLastExecution_WhenTaskHasExecuted"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 4: Run the full new test class together**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTaskStatusHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: build succeeds, `dotnet format` reports no changes needed, and all tests pass (no regressions in any other test class — this change touches only the new test file, so no other test should be affected).

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs
git commit -m "test: cover GetTaskStatusHandler happy-path last-execution mapping (FR-3)"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (task not found in registry) -> `fr1-not-found-test` task, `Handle_ReturnsNotFound_WhenTaskIdIsNotRegistered`. Covers `Found=false`, `Status=null`, no throw, `GetLastExecution` never called (`Times.Never()`).
- FR-2 (task registered, no last execution) -> `fr2-no-last-execution-test` task, `Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted`. Covers `Found=true`, non-null `Status`, `Status.LastExecution=null`, and pass-through `TaskId`/`Enabled`/`RefreshInterval`.
- FR-3 (happy path, populated last execution) -> `fr3-happy-path-test` task, `Handle_ReturnsFoundWithMappedLastExecution_WhenTaskHasExecuted`. Covers `Found=true`, non-null `LastExecution`, and every `RefreshTaskExecutionLogDto` field (`TaskId`, `StartedAt`, `CompletedAt`, `Status.ToString()`, `ErrorMessage`, `Duration`, `Metadata`).
- Spec's "Data Model" and "API / Interface Design" sections require no new types and confirm the `MakeSut()` signature has no logger — both reflected in `setup-test-file`.
- Spec's "Out of Scope" (no production code changes, no frontend changes, no changes to `GetBackgroundRefreshTasksHandler`, no integration/E2E tests) is respected: every task only touches the one new test file.
No gaps found.

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" placeholders. Every step has complete, runnable code or an exact command with expected output. No "similar to Task N" references — each task's test code is written out in full.

**3. Type consistency:** `MakeSut()`, `MakeTaskConfig()`, and `MakeExecutionLog()` are defined once in `setup-test-file` and reused unchanged (by name and signature) across `fr1-not-found-test`, `fr2-no-last-execution-test`, and `fr3-happy-path-test`. Property names (`Found`, `Status`, `TaskId`, `Enabled`, `RefreshInterval`, `LastExecution`, `StartedAt`, `CompletedAt`, `Status` (string), `ErrorMessage`, `Duration`, `Metadata`) match the actual source types read from `GetTaskStatusResponse.cs`, `RefreshTaskStatusDto.cs`, and `RefreshTaskExecutionLogDto.cs` verbatim — no invented members.
