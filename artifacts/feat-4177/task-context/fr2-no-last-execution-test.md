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

