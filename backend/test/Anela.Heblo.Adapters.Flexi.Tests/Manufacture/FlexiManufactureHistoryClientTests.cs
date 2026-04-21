using Anela.Heblo.Adapters.Flexi.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureHistoryClientTests
{
    private readonly Mock<IStockItemsMovementClient> _mockMovementClient;
    private readonly Mock<ILogger<FlexiManufactureHistoryClient>> _mockLogger;
    private readonly FlexiManufactureHistoryClient _client;

    public FlexiManufactureHistoryClientTests()
    {
        _mockMovementClient = new Mock<IStockItemsMovementClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureHistoryClient>>();

        _client = new FlexiManufactureHistoryClient(
            _mockMovementClient.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows()
    {
        // Arrange — HttpClient internal timeout (not caller's token)
        var timeoutCts = new CancellationTokenSource();
        var timeoutException = new TaskCanceledException("HttpClient timeout", null, timeoutCts.Token);
        await timeoutCts.CancelAsync();

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act — caller's token is NOT canceled
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, cancellationToken: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("timed out")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenCallerCancels_LogsInformationAndRethrows()
    {
        // Arrange — caller cancels via their own token
        using var callerCts = new CancellationTokenSource();
        var canceledException = new TaskCanceledException("Caller canceled", null, callerCts.Token);
        await callerCts.CancelAsync();

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(canceledException);

        // Act — same token is canceled
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, cancellationToken: callerCts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("canceled by the caller")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
