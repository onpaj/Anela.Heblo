using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.BackgroundRefresh;

public class RunHydrationTierHandlerTests
{
    private static (RunHydrationTierHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry, Mock<ILogger<RunHydrationTierHandler>> Logger) MakeSut()
    {
        var registry = new Mock<IBackgroundRefreshTaskRegistry>();
        var logger = new Mock<ILogger<RunHydrationTierHandler>>();
        var sut = new RunHydrationTierHandler(registry.Object, logger.Object);
        return (sut, registry, logger);
    }

    private static void VerifyLogged(Mock<ILogger<RunHydrationTierHandler>> logger, LogLevel level, Times times) =>
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            times);

    private static RefreshTaskConfiguration MakeTaskConfig(string taskId, int hydrationTier, bool enabled = true) =>
        new()
        {
            TaskId = taskId,
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.Zero,
            Enabled = enabled,
            HydrationTier = hydrationTier,
        };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoEnabledTasksInTier()
    {
        var (sut, registry, _) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>());

        var response = await sut.Handle(new RunHydrationTierRequest { Tier = 2 }, default);

        response.NotFound.Should().BeTrue();
        response.ErrorMessage.Should().NotBeNullOrEmpty();
        response.ErrorMessage.Should().Contain("2");
        response.TaskCount.Should().Be(0);
        response.Cancelled.Should().BeFalse();
        registry.Verify(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTasksInTierAreAllDisabled()
    {
        var (sut, registry, _) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig("task-a", hydrationTier: 2, enabled: false),
            MakeTaskConfig("task-b", hydrationTier: 2, enabled: false),
        });

        var response = await sut.Handle(new RunHydrationTierRequest { Tier = 2 }, default);

        response.NotFound.Should().BeTrue();
        response.ErrorMessage.Should().NotBeNullOrEmpty();
        response.ErrorMessage.Should().Contain("2");
        response.TaskCount.Should().Be(0);
        response.Cancelled.Should().BeFalse();
        registry.Verify(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully()
    {
        var (sut, registry, logger) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig("task-a", hydrationTier: 1),
            MakeTaskConfig("task-b", hydrationTier: 1),
            MakeTaskConfig("task-other", hydrationTier: 99),
        });
        registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await sut.Handle(new RunHydrationTierRequest { Tier = 1 }, default);

        response.TaskCount.Should().Be(2);
        response.NotFound.Should().BeFalse();
        response.Cancelled.Should().BeFalse();
        response.Success.Should().BeTrue();
        registry.Verify(r => r.ForceRefreshAsync("task-a", It.IsAny<CancellationToken>()), Times.Once);
        registry.Verify(r => r.ForceRefreshAsync("task-b", It.IsAny<CancellationToken>()), Times.Once);
        registry.Verify(r => r.ForceRefreshAsync("task-other", It.IsAny<CancellationToken>()), Times.Never);
        VerifyLogged(logger, LogLevel.Information, Times.Once());
    }

    [Fact]
    public async Task Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown()
    {
        var (sut, registry, _) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig("task-a", hydrationTier: 1),
        });
        registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var response = await sut.Handle(new RunHydrationTierRequest { Tier = 1 }, default);

        response.Cancelled.Should().BeTrue();
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsCancelled_WhenTokenAlreadyCancelled()
    {
        var (sut, registry, _) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig("task-a", hydrationTier: 1),
            MakeTaskConfig("task-b", hydrationTier: 1),
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await sut.Handle(new RunHydrationTierRequest { Tier = 1 }, cts.Token);

        response.Cancelled.Should().BeTrue();
        registry.Verify(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException()
    {
        var (sut, registry, logger) = MakeSut();
        registry.Setup(r => r.GetRegisteredTasks()).Returns(new List<RefreshTaskConfiguration>
        {
            MakeTaskConfig("task-a", hydrationTier: 1),
        });
        registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await sut.Handle(new RunHydrationTierRequest { Tier = 1 }, default);

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("An unexpected error occurred during tier hydration");
        response.Cancelled.Should().BeFalse();
        response.NotFound.Should().BeFalse();
        VerifyLogged(logger, LogLevel.Error, Times.Once());
        response.ErrorMessage.Should().NotContain("boom");
    }
}
