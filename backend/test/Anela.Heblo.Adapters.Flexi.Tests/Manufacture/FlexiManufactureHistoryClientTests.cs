using Anela.Heblo.Adapters.Flexi.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureHistoryClientTests
{
    private readonly Mock<IStockItemsMovementClient> _mockMovementClient;
    private readonly Mock<ILogger<FlexiManufactureHistoryClient>> _mockLogger;
    private readonly IMemoryCache _cache;
    private readonly FlexiManufactureHistoryClient _client;

    public FlexiManufactureHistoryClientTests()
    {
        _mockMovementClient = new Mock<IStockItemsMovementClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureHistoryClient>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _client = new FlexiManufactureHistoryClient(
            _mockMovementClient.Object,
            _cache,
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
    public async Task GetHistoryAsync_SecondCallWithSameDates_ReturnsCachedResultWithoutHittingFlexiBee()
    {
        // Arrange
        var dateFrom = new DateTime(2024, 1, 1);
        var dateTo = new DateTime(2024, 3, 31);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockItemMovementFlexiDto>());

        // Act — call twice with the same parameters
        var result1 = await _client.GetHistoryAsync(dateFrom, dateTo);
        var result2 = await _client.GetHistoryAsync(dateFrom, dateTo);

        // Assert — FlexiBee called only once; second call served from cache
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result1.Should().BeEquivalentTo(result2);
    }

    [Fact]
    public async Task GetHistoryAsync_AfterInvalidate_HitsFlexiBeeAgain()
    {
        // Arrange
        var dateFrom = new DateTime(2024, 1, 1);
        var dateTo = new DateTime(2024, 3, 31);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockItemMovementFlexiDto>());

        // Act — first call populates cache, then invalidate, then second call should hit FlexiBee
        await _client.GetHistoryAsync(dateFrom, dateTo);
        _client.Invalidate();
        await _client.GetHistoryAsync(dateFrom, dateTo);

        // Assert — FlexiBee called twice: once before invalidate, once after
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public void Invalidate_WhenCalledMultipleTimes_DoesNotThrow()
    {
        // Act & Assert — no exception on repeated invalidations
        var act = () =>
        {
            _client.Invalidate();
            _client.Invalidate();
            _client.Invalidate();
        };

        act.Should().NotThrow();
    }
}
