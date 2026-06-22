using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureCatalogSourceAdapterTests
{
    private readonly Mock<IManufactureOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IManufactureHistoryClient> _historyClientMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock = new();
    private readonly ManufactureCatalogSourceAdapter _adapter;

    public ManufactureCatalogSourceAdapterTests()
    {
        _adapter = new ManufactureCatalogSourceAdapter(
            _orderRepositoryMock.Object,
            _historyClientMock.Object,
            _inventoryRepositoryMock.Object);
    }

    [Fact]
    public async Task GetPlannedQuantitiesAsync_DelegatesToOrderRepository()
    {
        var expected = new Dictionary<string, decimal> { ["PROD-A"] = 3m };
        _orderRepositoryMock
            .Setup(r => r.GetPlannedQuantitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _adapter.GetPlannedQuantitiesAsync(CancellationToken.None);

        result.Should().BeSameAs(expected);
        _orderRepositoryMock.Verify(r => r.GetPlannedQuantitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetManufactureHistoryAsync_MapsManufactureHistoryRecordToCatalogManufactureRecord()
    {
        var dateFrom = new DateTime(2026, 1, 1);
        var dateTo = new DateTime(2026, 2, 1);
        var sourceRecords = new List<ManufactureHistoryRecord>
        {
            new() { ProductCode = "PROD-A", Date = dateFrom, Amount = 5, PricePerPiece = 10m, PriceTotal = 50m, DocumentNumber = "DOC-001" },
        };
        _historyClientMock
            .Setup(c => c.GetHistoryAsync(dateFrom, dateTo, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceRecords);

        var result = await _adapter.GetManufactureHistoryAsync(dateFrom, dateTo, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new CatalogManufactureRecord
        {
            ProductCode = "PROD-A",
            Date = dateFrom,
            Amount = 5,
            PricePerPiece = 10m,
            PriceTotal = 50m,
            DocumentNumber = "DOC-001",
        });
        _historyClientMock.Verify(
            c => c.GetHistoryAsync(dateFrom, dateTo, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetManufacturedInventoryAsync_DelegatesToInventoryRepository()
    {
        var expected = new Dictionary<string, decimal> { ["PROD-X"] = 99m };
        _inventoryRepositoryMock
            .Setup(r => r.GetTotalAmountByProductCodeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _adapter.GetManufacturedInventoryAsync(CancellationToken.None);

        result.Should().BeSameAs(expected);
        _inventoryRepositoryMock.Verify(r => r.GetTotalAmountByProductCodeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
