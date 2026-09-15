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
}
