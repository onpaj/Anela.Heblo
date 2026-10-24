using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.BackgroundRefresh;

public class ForceRefreshTaskHandlerTests
{
    private static (ForceRefreshTaskHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry, Mock<ILogger<ForceRefreshTaskHandler>> Logger) MakeSut()
    {
        var registry = new Mock<IBackgroundRefreshTaskRegistry>();
        var logger = new Mock<ILogger<ForceRefreshTaskHandler>>();
        var sut = new ForceRefreshTaskHandler(registry.Object, logger.Object);
        return (sut, registry, logger);
    }

    private static void VerifyLogged(Mock<ILogger<ForceRefreshTaskHandler>> logger, LogLevel level, Times times) =>
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            times);

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenRefreshCompletes()
    {
        var (sut, registry, _) = MakeSut();
        using var cts = new CancellationTokenSource();
        registry.Setup(r => r.ForceRefreshAsync("task-a", cts.Token)).Returns(Task.CompletedTask);

        var response = await sut.Handle(new ForceRefreshTaskRequest { TaskId = "task-a" }, cts.Token);

        response.Success.Should().BeTrue();
        response.NotFound.Should().BeFalse();
        response.ErrorMessage.Should().BeNull();
        registry.Verify(r => r.ForceRefreshAsync("task-a", cts.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTaskNotRegistered()
    {
        var (sut, registry, logger) = MakeSut();
        const string message = "Task with ID 'missing' is not registered";
        registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

        var response = await sut.Handle(new ForceRefreshTaskRequest { TaskId = "missing" }, default);

        response.NotFound.Should().BeTrue();
        response.ErrorMessage.Should().Be(message);
        VerifyLogged(logger, LogLevel.Warning, Times.Once());
        VerifyLogged(logger, LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenSubclassOfInvalidOperationThrown()
    {
        var (sut, registry, _) = MakeSut();
        registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObjectDisposedException("registry"));

        var response = await sut.Handle(new ForceRefreshTaskRequest { TaskId = "task-a" }, default);

        response.NotFound.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(UnexpectedExceptions))]
    public async Task Handle_ReturnsGenericError_WhenUnexpectedExceptionThrown(Exception exception)
    {
        var (sut, registry, logger) = MakeSut();
        registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var response = await sut.Handle(new ForceRefreshTaskRequest { TaskId = "task-a" }, default);

        response.Success.Should().BeFalse();
        response.NotFound.Should().BeFalse();
        response.ErrorMessage.Should().Be("An unexpected error occurred during force refresh");
        response.ErrorMessage.Should().NotContain(exception.Message);
        VerifyLogged(logger, LogLevel.Error, Times.Once());
        VerifyLogged(logger, LogLevel.Warning, Times.Never());
    }

    public static IEnumerable<object[]> UnexpectedExceptions()
    {
        yield return new object[] { new Exception("secret-connection-string") };
        yield return new object[] { new TimeoutException("secret-connection-string") };
    }
}
