using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class StockValueServiceTests
{
    private readonly Mock<IErpStockClient> _stockClientMock;
    private readonly Mock<ILogger<FinancialOverviewStockValueAdapter>> _loggerMock;
    private readonly FinancialOverviewStockValueAdapter _service;

    public StockValueServiceTests()
    {
        _stockClientMock = new Mock<IErpStockClient>();
        _loggerMock = new Mock<ILogger<FinancialOverviewStockValueAdapter>>();

        _service = new FinancialOverviewStockValueAdapter(
            _stockClientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_CalculatesCorrectStockValueChanges()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 2, 29); // 2 months

        // Setup stock data (valued at MAT001 = 100, SEMI001 = 200, PROD001 = 300) - January start vs end
        SetupStockDataForJanuary();

        // Setup stock data - February start vs end  
        SetupStockDataForFebruary();

        // Act
        var result = await _service.GetStockValueChangesAsync(startDate, endDate, CancellationToken.None);

        // Assert
        result.Count.Should().Be(2);

        // January changes
        var january = result.First(x => x.Month == 1 && x.Year == 2024);
        january.StockChanges.Materials.Should().Be(1000m); // (20-10) * 100 = 1000
        january.StockChanges.SemiProducts.Should().Be(600m); // (8-5) * 200 = 600
        january.StockChanges.Products.Should().Be(900m); // (7-4) * 300 = 900
        january.TotalStockValueChange.Should().Be(2500m);

        // February changes
        var february = result.First(x => x.Month == 2 && x.Year == 2024);
        february.StockChanges.Materials.Should().Be(-500m); // (15-20) * 100 = -500
        february.StockChanges.SemiProducts.Should().Be(-400m); // (6-8) * 200 = -400
        february.StockChanges.Products.Should().Be(-300m); // (6-7) * 300 = -300
        february.TotalStockValueChange.Should().Be(-1200m);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_HandlesEmptyStockData()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);

        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());

        // Act
        var result = await _service.GetStockValueChangesAsync(startDate, endDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var january = result.First();
        january.TotalStockValueChange.Should().Be(0m);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_HandlesRowsWithoutWarehousePrice()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);

        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());
        // Materials warehouse (ID 5): the unpriced rows move a lot, the priced row moves by 2 units
        _stockClientMock.Setup(x => x.StockToDateAsync(startDate, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 10m, Price = 100m },
                new() { ProductCode = "MAT002", Stock = 5m }, // No warehouse valuation
                new() { ProductCode = "MAT003", Stock = 3m }  // No warehouse valuation
            });
        _stockClientMock.Setup(x => x.StockToDateAsync(endDate, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 12m, Price = 100m },
                new() { ProductCode = "MAT002", Stock = 500m }, // No warehouse valuation
                new() { ProductCode = "MAT003", Stock = 300m }  // No warehouse valuation
            });

        // Act
        var result = await _service.GetStockValueChangesAsync(startDate, endDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var january = result.First();
        // Only MAT001 carries a warehouse price; unpriced rows contribute nothing however much they move
        january.StockChanges.Materials.Should().Be(200m);
        january.TotalStockValueChange.Should().Be(200m);
    }

    [Fact]
    public async Task GetStockValueChangeForPeriodAsync_UsesPeriodEndDate_ForEndSnapshot()
    {
        // Arrange
        var periodStart = new DateTime(2025, 7, 1);
        var periodEnd = new DateTime(2025, 7, 10); // partial month cut at day 10

        // Every warehouse/date returns empty by default...
        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());
        // ...except Materials (ID 5) at the two period boundaries.
        _stockClientMock.Setup(x => x.StockToDateAsync(periodStart, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "MAT001", Stock = 10m, Price = 100m } });
        _stockClientMock.Setup(x => x.StockToDateAsync(periodEnd, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "MAT001", Stock = 13m, Price = 100m } });

        // Act
        var result = await _service.GetStockValueChangeForPeriodAsync(periodStart, periodEnd, CancellationToken.None);

        // Assert
        result.Year.Should().Be(2025);
        result.Month.Should().Be(7);
        result.StockChanges.Materials.Should().Be(300m); // (13 - 10) * 100
        result.TotalStockValueChange.Should().Be(300m);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_ValuesStockAtWarehousePrice_NotCenikPurchasePrice()
    {
        // Arrange
        var monthStart = new DateTime(2026, 9, 1);
        var monthEnd = new DateTime(2026, 9, 30);

        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());
        _stockClientMock.Setup(x => x.StockToDateAsync(monthStart, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "DEZ001100", Stock = 0m, Price = 0m } });
        _stockClientMock.Setup(x => x.StockToDateAsync(monthEnd, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "DEZ001100", Stock = 58m, Price = 62.35m } });

        // Act
        var result = await _service.GetStockValueChangesAsync(monthStart, monthEnd, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].StockChanges.Products.Should().Be(58m * 62.35m); // 3616.30, not 58 x ceník 41.93
    }

    [Fact]
    public async Task GetStockValueChangeForPeriodAsync_ValuesEachSnapshotAtItsOwnWarehousePrice()
    {
        // Arrange
        var periodStart = new DateTime(2026, 9, 1);
        var periodEnd = new DateTime(2026, 9, 15);

        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());
        // The average price moves between the two snapshots; each is valued at its own date's price.
        _stockClientMock.Setup(x => x.StockToDateAsync(periodStart, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "SEMI001", Stock = 10m, Price = 1.5m } });
        _stockClientMock.Setup(x => x.StockToDateAsync(periodEnd, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "SEMI001", Stock = 12m, Price = 2.25m } });

        // Act
        var result = await _service.GetStockValueChangeForPeriodAsync(periodStart, periodEnd, CancellationToken.None);

        // Assert
        result.StockChanges.SemiProducts.Should().Be(12m * 2.25m - 10m * 1.5m); // 27 - 15 = 12
        result.StockChanges.Materials.Should().Be(0m);
        result.StockChanges.Products.Should().Be(0m);
    }

    private void SetupStockDataForJanuary()
    {
        var januaryStart = new DateTime(2024, 1, 1);
        var januaryEnd = new DateTime(2024, 1, 31);

        // Materials warehouse (ID 5) - January start
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryStart, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 10m, Price = 100m }
            });

        // Materials warehouse (ID 5) - January end  
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryEnd, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 20m, Price = 100m }
            });

        // Semi-products warehouse (ID 20) - January start
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryStart, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 5m, Price = 200m }
            });

        // Semi-products warehouse (ID 20) - January end
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryEnd, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 8m, Price = 200m }
            });

        // Products warehouse (ID 4) - January start
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryStart, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 4m, Price = 300m }
            });

        // Products warehouse (ID 4) - January end
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryEnd, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 7m, Price = 300m }
            });
    }

    private void SetupStockDataForFebruary()
    {
        var februaryStart = new DateTime(2024, 2, 1);
        var februaryEnd = new DateTime(2024, 2, 29);

        // Materials warehouse (ID 5) - February start (same as January end)
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryStart, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 20m, Price = 100m }
            });

        // Materials warehouse (ID 5) - February end
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryEnd, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 15m, Price = 100m }
            });

        // Semi-products warehouse (ID 20) - February start (same as January end)
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryStart, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 8m, Price = 200m }
            });

        // Semi-products warehouse (ID 20) - February end
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryEnd, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 6m, Price = 200m }
            });

        // Products warehouse (ID 4) - February start (same as January end)
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryStart, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 7m, Price = 300m }
            });

        // Products warehouse (ID 4) - February end
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryEnd, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 6m, Price = 300m }
            });
    }
}