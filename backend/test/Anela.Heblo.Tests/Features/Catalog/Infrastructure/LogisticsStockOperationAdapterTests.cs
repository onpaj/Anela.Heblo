using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsStockOperationAdapterTests
{
    private readonly Mock<IStockUpProcessingService> _service = new();

    private LogisticsStockOperationAdapter CreateAdapter() => new(_service.Object);

    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CreateOperationAsync_WithTransportBoxSource_DelegatesToServiceWithCorrectEnum()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.TransportBox, 10);

        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_WithGiftPackageManufactureSource_DelegatesToServiceWithCorrectEnum()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.GiftPackageManufacture, 10);

        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_PassesThroughAllParameters()
    {
        var ct = new CancellationToken(false);
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-42", "SET-99", 7, LogisticsStockOperationSource.TransportBox, 55, ct);

        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException()
    {
        var unknownSource = (LogisticsStockOperationSource)999;

        var act = () => CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 1, unknownSource, 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
