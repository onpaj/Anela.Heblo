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

