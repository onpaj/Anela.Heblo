using Anela.Heblo.Adapters.Flexi.Accounting.Ledger;
using Anela.Heblo.Domain.Accounting.Ledger;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Accounting;

public class LedgerServiceTests
{
    private readonly Mock<ILedgerClient> _mockLedgerClient;
    private readonly Mock<ILogger<LedgerService>> _mockLogger;
    private readonly LedgerService _service;

    public LedgerServiceTests()
    {
        _mockLedgerClient = new Mock<ILedgerClient>();
        _mockLogger = new Mock<ILogger<LedgerService>>();
        var mockCache = new Mock<IMemoryCache>();
        var mockMapper = new Mock<IMapper>();

        // Cache always misses so we reach the HTTP call
        object? cacheOut = null;
        mockCache.Setup(x => x.TryGetValue(It.IsAny<object>(), out cacheOut)).Returns(false);
        mockCache.Setup(x => x.CreateEntry(It.IsAny<object>())).Returns(Mock.Of<ICacheEntry>());

        // Mapper returns empty list
        mockMapper
            .Setup(x => x.Map<List<LedgerItem>>(It.IsAny<object>()))
            .Returns(new List<LedgerItem>());

        _service = new LedgerService(
            _mockLedgerClient.Object,
            mockCache.Object,
            mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetLedgerItems_WhenInternalTimeoutCancels_LogsWarningAndRethrows()
    {
        // Arrange — HttpClient internal timeout: its own CTS fires, not the caller's
        var timeoutCts = new CancellationTokenSource();
        var timeoutException = new TaskCanceledException("HttpClient timeout", null, timeoutCts.Token);
        await timeoutCts.CancelAsync();

        _mockLedgerClient
            .Setup(x => x.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act — caller's token is NOT canceled
        var act = async () => await _service.GetLedgerItems(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            cancellationToken: CancellationToken.None);

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
    public async Task GetLedgerItems_WhenCallerCancels_LogsInformationAndRethrows()
    {
        // Arrange — caller cancels via their own token
        using var callerCts = new CancellationTokenSource();
        var canceledException = new TaskCanceledException("Caller canceled", null, callerCts.Token);
        await callerCts.CancelAsync();

        _mockLedgerClient
            .Setup(x => x.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(canceledException);

        // Act — same token is canceled
        var act = async () => await _service.GetLedgerItems(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            cancellationToken: callerCts.Token);

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
