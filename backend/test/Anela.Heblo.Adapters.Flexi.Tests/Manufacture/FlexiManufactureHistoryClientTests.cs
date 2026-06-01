using System.Net;
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

    [Fact]
    public async Task GetHistoryAsync_When503ThenSucceeds_RetriesAndReturnsResult()
    {
        // Arrange
        var transient = new HttpRequestException(
            "Service Unavailable",
            inner: null,
            statusCode: HttpStatusCode.ServiceUnavailable);

        _mockMovementClient
            .SetupSequence(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(transient)
            .ThrowsAsync(transient)
            .ReturnsAsync(new List<StockItemMovementFlexiDto>());

        // Act
        var result = await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetHistoryAsync_When503Persists_LogsWarningAndRethrowsAfterRetries()
    {
        // Arrange
        var transient = new HttpRequestException(
            "Service Unavailable",
            inner: null,
            statusCode: HttpStatusCode.ServiceUnavailable);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(transient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("returned transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When502Persists_RetriesAndLogsWarning()
    {
        // Arrange
        var transient = new HttpRequestException(
            "Bad Gateway",
            inner: null,
            statusCode: HttpStatusCode.BadGateway);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(transient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("returned transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When400_DoesNotRetry_LogsErrorAndRethrows()
    {
        // Arrange
        var nonTransient = new HttpRequestException(
            "Bad Request",
            inner: null,
            statusCode: HttpStatusCode.BadRequest);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(nonTransient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FlexiBee skladovy-pohyb-polozka returned") && !v.ToString()!.Contains("transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When500_DoesNotRetry_LogsErrorAndRethrows()
    {
        // Arrange
        var nonTransient = new HttpRequestException(
            "Internal Server Error",
            inner: null,
            statusCode: HttpStatusCode.InternalServerError);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(nonTransient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FlexiBee skladovy-pohyb-polozka returned") && !v.ToString()!.Contains("transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
