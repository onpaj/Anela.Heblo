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
}
