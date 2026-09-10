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
