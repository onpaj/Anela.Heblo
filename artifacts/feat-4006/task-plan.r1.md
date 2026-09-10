# GetBackgroundRefreshTasksHandler Unit Tests Implementation Plan

**Goal:** Add a unit test suite for `GetBackgroundRefreshTasksHandler.Handle` covering all four combinations of the `MapToDto` `NextScheduledRun` compound condition and both branches of the `LastExecution` null check, closing the existing coverage gap with no production code changes.

**Architecture:** A single new xUnit test class, `GetBackgroundRefreshTasksHandlerTests`, is added to `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/`, alongside the existing sibling `RunHydrationTierHandlerTests.cs` in the same directory/namespace. Each test constructs its own `Mock<IBackgroundRefreshTaskRegistry>` and `Mock<ILogger<GetBackgroundRefreshTasksHandler>>` via a private `MakeSut()` helper — no shared fixture, no `IClassFixture`. Two private fixture-builder helpers (`MakeTaskConfig`, `MakeExecutionLog`) reduce Arrange-block duplication, copying the parameterized-default-object-initializer style already used by the sibling test's `MakeTaskConfig` helper. All `DateTime` values used in assertions are fixed literals (no `DateTime.UtcNow`), per spec NFR-2.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions (all already referenced by the `Anela.Heblo.Tests` project — no new package references).

---

### task: add-getbackgroundrefreshtaskshandler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs`
- Test: same file (this is a test-only addition; there is no separate production file to modify)

Reference files read to produce this plan (do not modify):
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksHandler.cs` — confirmed: namespace `Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks`; constructor `GetBackgroundRefreshTasksHandler(IBackgroundRefreshTaskRegistry, ILogger<GetBackgroundRefreshTasksHandler>)`; `Handle` calls `_taskRegistry.GetRegisteredTasks()` then, per task, `_taskRegistry.GetLastExecution(task.TaskId)`, mapping each via the private `MapToDto`; `MapToDto`'s `NextScheduledRun` is set only when `task.Enabled && lastExecution?.CompletedAt != null`, computed as `lastExecution.CompletedAt.Value.Add(task.RefreshInterval)`; `LastExecution` is set only when `lastExecution != null`, via `MapToExecutionLogDto` which maps `TaskId`, `StartedAt`, `CompletedAt`, `Status.ToString()`, `ErrorMessage`, `Duration`, `Metadata`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksRequest.cs` — confirmed: `public class GetBackgroundRefreshTasksRequest : IRequest<GetBackgroundRefreshTasksResponse> { }` — no constructor arguments.
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksResponse.cs` — confirmed: `public class GetBackgroundRefreshTasksResponse : BaseResponse { public IReadOnlyList<RefreshTaskDto> Tasks { get; set; } = []; }`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs` — confirmed: `TaskId` (`required string`), `InitialDelay`/`RefreshInterval` (`required TimeSpan`), `Enabled` (`required bool`), `HydrationTier` (`int`, no `required`), `NextScheduledRun` (`DateTime?`), `LastExecution` (`RefreshTaskExecutionLogDto?`).
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs` — confirmed: `TaskId` (`required string`), `StartedAt` (`required DateTime`), `CompletedAt` (`DateTime?`), `Status` (`required string`), `ErrorMessage` (`string?`), `Duration` (`TimeSpan?`), `Metadata` (`Dictionary<string, object>?`).
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/IBackgroundRefreshTaskRegistry.cs` — confirmed the two members `Handle` depends on: `IReadOnlyList<RefreshTaskConfiguration> GetRegisteredTasks()` and `RefreshTaskExecutionLog? GetLastExecution(string taskId)`.
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskConfiguration.cs` — confirmed: `TaskId` (`required string`), `InitialDelay`/`RefreshInterval` (`required TimeSpan`), `Enabled` (`required bool`), `HydrationTier` (`int`, defaults to `1`).
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskExecutionLog.cs` — confirmed this is a `record`: `TaskId` (`required string`), `StartedAt` (`required DateTime`), `CompletedAt` (`DateTime?`), `Status` (`required RefreshTaskExecutionStatus`), `ErrorMessage` (`string?`), `Duration` (computed, `CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null` — do not set directly), `Metadata` (`Dictionary<string, object>?`).
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskExecutionStatus.cs` — confirmed enum values: `Running, Completed, Failed, Cancelled`, namespace `Anela.Heblo.Xcc.Services.BackgroundRefresh`.
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs` — style template: namespace `Anela.Heblo.Tests.Application.BackgroundRefresh`, plain xUnit `[Fact]`s (no `[Theory]`), a `MakeSut()` tuple-returning helper, a `MakeTaskConfig(...)` fixture builder with named-optional-parameter defaults, `FluentAssertions`'s `.Should().Be(...)`/`.Should().BeTrue()` style, `Mock<IBackgroundRefreshTaskRegistry>` + `Mock<ILogger<T>>` constructed directly (no `IClassFixture`).
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — confirmed `ImplicitUsings` and `Nullable` both `enable`, `<Using Include="Xunit" />` global using already present, Moq 4.20.72 and FluentAssertions 6.12.0 already referenced; no `TreatWarningsAsErrors`.

---

- [ ] **Step 1: Write the failing test file with all test cases**

  Create `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs` with the following exact content:

  ```csharp
  using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;
  using Anela.Heblo.Xcc.Services.BackgroundRefresh;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Moq;

  namespace Anela.Heblo.Tests.Application.BackgroundRefresh;

  public class GetBackgroundRefreshTasksHandlerTests
  {
      private static (GetBackgroundRefreshTasksHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry) MakeSut()
      {
          var registry = new Mock<IBackgroundRefreshTaskRegistry>();
          var logger = new Mock<ILogger<GetBackgroundRefreshTasksHandler>>();
          var sut = new GetBackgroundRefreshTasksHandler(registry.Object, logger.Object);
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
          RefreshTaskExecutionStatus status = RefreshTaskExecutionStatus.Completed) =>
          new()
          {
              TaskId = taskId,
              StartedAt = startedAt ?? new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
              CompletedAt = completedAt,
              Status = status,
              ErrorMessage = null,
              Metadata = null,
          };

      // FR-2 case 1: disabled task always yields NextScheduledRun == null, even with a completed last execution.
      [Fact]
      public async Task Handle_NextScheduledRunIsNull_WhenTaskIsDisabled()
      {
          var (sut, registry) = MakeSut();
          var completedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              MakeTaskConfig(enabled: false),
          });
          registry.Setup(r => r.GetLastExecution("task-a"))
              .Returns(MakeExecutionLog(completedAt: completedAt));

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          response.Tasks.Single().NextScheduledRun.Should().BeNull();
      }

      // FR-2 case 2 / FR-3 case 1: enabled task with no last execution at all yields both null.
      [Fact]
      public async Task Handle_NextScheduledRunAndLastExecutionAreNull_WhenNoLastExecutionExists()
      {
          var (sut, registry) = MakeSut();
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              MakeTaskConfig(enabled: true),
          });
          registry.Setup(r => r.GetLastExecution("task-a"))
              .Returns((RefreshTaskExecutionLog?)null);

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          var dto = response.Tasks.Single();
          dto.NextScheduledRun.Should().BeNull();
          dto.LastExecution.Should().BeNull();
      }

      // FR-2 case 3: enabled task with an in-flight (not yet completed) last execution yields NextScheduledRun == null.
      [Fact]
      public async Task Handle_NextScheduledRunIsNull_WhenLastExecutionHasNotCompleted()
      {
          var (sut, registry) = MakeSut();
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              MakeTaskConfig(enabled: true),
          });
          registry.Setup(r => r.GetLastExecution("task-a"))
              .Returns(MakeExecutionLog(completedAt: null, status: RefreshTaskExecutionStatus.Running));

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          response.Tasks.Single().NextScheduledRun.Should().BeNull();
      }

      // FR-2 case 4: enabled task with a completed last execution yields NextScheduledRun == CompletedAt + RefreshInterval, exactly.
      [Fact]
      public async Task Handle_NextScheduledRunEqualsCompletedAtPlusRefreshInterval_WhenTaskEnabledAndLastExecutionCompleted()
      {
          var (sut, registry) = MakeSut();
          var completedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
          var refreshInterval = TimeSpan.FromHours(4);
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              MakeTaskConfig(enabled: true, refreshInterval: refreshInterval),
          });
          registry.Setup(r => r.GetLastExecution("task-a"))
              .Returns(MakeExecutionLog(completedAt: completedAt));

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          response.Tasks.Single().NextScheduledRun.Should().Be(completedAt.Add(refreshInterval));
      }

      // FR-3 case 2: when a last execution exists, every LastExecution field is mapped from the source log.
      [Fact]
      public async Task Handle_MapsLastExecutionFields_WhenLastExecutionExists()
      {
          var (sut, registry) = MakeSut();
          var startedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
          var completedAt = new DateTime(2026, 1, 1, 9, 5, 0, DateTimeKind.Utc);
          var metadata = new Dictionary<string, object> { ["rows"] = 42 };
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              MakeTaskConfig(enabled: true),
          });
          registry.Setup(r => r.GetLastExecution("task-a")).Returns(new RefreshTaskExecutionLog
          {
              TaskId = "task-a",
              StartedAt = startedAt,
              CompletedAt = completedAt,
              Status = RefreshTaskExecutionStatus.Failed,
              ErrorMessage = "boom",
              Metadata = metadata,
          });

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          var lastExecution = response.Tasks.Single().LastExecution;
          lastExecution.Should().NotBeNull();
          lastExecution!.TaskId.Should().Be("task-a");
          lastExecution.StartedAt.Should().Be(startedAt);
          lastExecution.CompletedAt.Should().Be(completedAt);
          lastExecution.Status.Should().Be(RefreshTaskExecutionStatus.Failed.ToString());
          lastExecution.ErrorMessage.Should().Be("boom");
          lastExecution.Duration.Should().Be(completedAt - startedAt);
          lastExecution.Metadata.Should().BeEquivalentTo(metadata);
      }

      // FR-4: pass-through fields (TaskId, InitialDelay, RefreshInterval, Enabled, HydrationTier) map unchanged.
      [Fact]
      public async Task Handle_MapsPassThroughFields_FromConfigurationToDto()
      {
          var (sut, registry) = MakeSut();
          var initialDelay = TimeSpan.FromSeconds(30);
          var refreshInterval = TimeSpan.FromMinutes(15);
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              new()
              {
                  TaskId = "task-passthrough",
                  InitialDelay = initialDelay,
                  RefreshInterval = refreshInterval,
                  Enabled = true,
                  HydrationTier = 3,
              },
          });
          registry.Setup(r => r.GetLastExecution("task-passthrough"))
              .Returns((RefreshTaskExecutionLog?)null);

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          var dto = response.Tasks.Single();
          dto.TaskId.Should().Be("task-passthrough");
          dto.InitialDelay.Should().Be(initialDelay);
          dto.RefreshInterval.Should().Be(refreshInterval);
          dto.Enabled.Should().BeTrue();
          dto.HydrationTier.Should().Be(3);
      }

      // FR-5: multiple tasks are mapped independently -- one task's Enabled/lastExecution never leaks into another's DTO.
      [Fact]
      public async Task Handle_MapsEachTaskIndependently_WhenMultipleTasksRegistered()
      {
          var (sut, registry) = MakeSut();
          var completedAtA = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
          var refreshIntervalA = TimeSpan.FromHours(2);
          registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
          {
              MakeTaskConfig(taskId: "task-a", enabled: true, refreshInterval: refreshIntervalA),
              MakeTaskConfig(taskId: "task-b", enabled: false),
              MakeTaskConfig(taskId: "task-c", enabled: true),
          });
          registry.Setup(r => r.GetLastExecution("task-a"))
              .Returns(MakeExecutionLog(taskId: "task-a", completedAt: completedAtA));
          registry.Setup(r => r.GetLastExecution("task-b"))
              .Returns(MakeExecutionLog(taskId: "task-b", completedAt: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
          registry.Setup(r => r.GetLastExecution("task-c"))
              .Returns((RefreshTaskExecutionLog?)null);

          var response = await sut.Handle(new GetBackgroundRefreshTasksRequest(), default);

          response.Tasks.Should().HaveCount(3);
          var dtoA = response.Tasks.Single(t => t.TaskId == "task-a");
          var dtoB = response.Tasks.Single(t => t.TaskId == "task-b");
          var dtoC = response.Tasks.Single(t => t.TaskId == "task-c");

          dtoA.NextScheduledRun.Should().Be(completedAtA.Add(refreshIntervalA));
          dtoA.LastExecution.Should().NotBeNull();

          dtoB.NextScheduledRun.Should().BeNull(); // disabled, despite having a completed execution
          dtoB.LastExecution.Should().NotBeNull();

          dtoC.NextScheduledRun.Should().BeNull(); // enabled but no execution recorded
          dtoC.LastExecution.Should().BeNull();
      }
  }
  ```

- [ ] **Step 2: Run the new tests to verify they pass**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.BackgroundRefresh.GetBackgroundRefreshTasksHandlerTests"
  ```

  Expected output: 7 tests discovered and passed (`Handle_NextScheduledRunIsNull_WhenTaskIsDisabled`, `Handle_NextScheduledRunAndLastExecutionAreNull_WhenNoLastExecutionExists`, `Handle_NextScheduledRunIsNull_WhenLastExecutionHasNotCompleted`, `Handle_NextScheduledRunEqualsCompletedAtPlusRefreshInterval_WhenTaskEnabledAndLastExecutionCompleted`, `Handle_MapsLastExecutionFields_WhenLastExecutionExists`, `Handle_MapsPassThroughFields_FromConfigurationToDto`, `Handle_MapsEachTaskIndependently_WhenMultipleTasksRegistered`), 0 failed.

- [ ] **Step 3: Run the full sibling test folder to confirm no regressions**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.BackgroundRefresh"
  ```

  Expected output: all tests in `Application.BackgroundRefresh` pass, including the existing `RunHydrationTierHandlerTests` plus the 7 new `GetBackgroundRefreshTasksHandlerTests`.

- [ ] **Step 4: Run `dotnet format` to confirm formatting compliance**

  ```bash
  cd backend
  dotnet format --verify-no-changes --include test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs
  ```

  If this reports changes needed, run `dotnet format --include test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs` (without `--verify-no-changes`) to apply them, then re-run Step 2 to confirm the tests still pass after formatting.

- [ ] **Step 5: Commit**

  ```bash
  git add backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs
  git commit -m "test(background-refresh): add GetBackgroundRefreshTasksHandler unit test coverage"
  ```

  Verify: `git show --stat HEAD`

  Expected: a single file, `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs`, listed as added.
