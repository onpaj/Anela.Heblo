using System.Net;
using Anela.Heblo.Adapters.Flexi.Stock;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Stock;

public class FlexiStockClientTests
{
    private readonly Mock<IStockToDateClient> _mockStockClient;
    private readonly Mock<ILogger<FlexiStockClient>> _mockLogger;
    private readonly FlexiStockClient _client;

    public FlexiStockClientTests()
    {
        _mockStockClient = new Mock<IStockToDateClient>();
        _mockLogger = new Mock<ILogger<FlexiStockClient>>();
        var mockMapper = new Mock<IMapper>();

        _client = new FlexiStockClient(
            _mockStockClient.Object,
            TimeProvider.System,
            mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task StockToDateAsync_When501Returned_LogsErrorAndRethrows()
    {
        // Arrange
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockStockClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _client.StockToDateAsync(DateTime.UtcNow, warehouseId: 5, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("501") && v.ToString()!.Contains("stav-skladu-k-datu")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ListAsync_When501ReturnedForWarehouse_LogsErrorAndRethrows()
    {
        // Arrange
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockStockClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("501") && v.ToString()!.Contains("stav-skladu-k-datu")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task StockToDateAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows()
    {
        // Arrange — timeout fires from HttpClient's own CTS, not from our token
        var timeoutCts = new CancellationTokenSource();
        var timeoutException = new TaskCanceledException("HttpClient timeout", null, timeoutCts.Token);
        await timeoutCts.CancelAsync();

        _mockStockClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act — caller's token is NOT canceled
        var act = async () => await _client.StockToDateAsync(DateTime.UtcNow, warehouseId: 5, CancellationToken.None);

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
    public async Task StockToDateAsync_WhenCallerCancels_LogsInformationAndRethrows()
    {
        // Arrange — caller cancels via their own token
        using var callerCts = new CancellationTokenSource();
        var canceledException = new TaskCanceledException("Caller canceled", null, callerCts.Token);
        await callerCts.CancelAsync();

        _mockStockClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(canceledException);

        // Act — same token is canceled
        var act = async () => await _client.StockToDateAsync(DateTime.UtcNow, warehouseId: 5, callerCts.Token);

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
    public async Task ListAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows()
    {
        // Arrange — HttpClient internal timeout (not caller's token)
        var timeoutCts = new CancellationTokenSource();
        var timeoutException = new TaskCanceledException("HttpClient timeout", null, timeoutCts.Token);
        await timeoutCts.CancelAsync();

        _mockStockClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act
        var act = async () => await _client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("timed out")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ListAsync_WhenCallerCancels_LogsInformationAndRethrows()
    {
        // Arrange
        using var callerCts = new CancellationTokenSource();
        var canceledException = new TaskCanceledException("Caller canceled", null, callerCts.Token);
        await callerCts.CancelAsync();

        _mockStockClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(canceledException);

        // Act
        var act = async () => await _client.ListAsync(callerCts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("canceled by the caller")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
