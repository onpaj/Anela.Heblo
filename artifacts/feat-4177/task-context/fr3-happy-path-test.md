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
