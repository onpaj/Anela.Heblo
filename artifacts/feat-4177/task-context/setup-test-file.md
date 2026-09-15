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

