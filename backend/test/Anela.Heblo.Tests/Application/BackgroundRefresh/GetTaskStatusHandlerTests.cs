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
