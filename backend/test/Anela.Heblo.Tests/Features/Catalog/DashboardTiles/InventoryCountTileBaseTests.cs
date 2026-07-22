using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DashboardTiles;

public class InventoryCountTileBaseTests
{
    private static readonly DateTimeOffset FrozenNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadDataAsync_ItemAtExactCutoff_IsIncluded()
    {
        // Arrange
        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        var timeProvider = new FakeTimeProvider(FrozenNow);
        var tile = new ProductInventoryCountTile(catalogRepositoryMock.Object, timeProvider);

        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, FrozenNow.UtcDateTime.AddDays(-30))
        };

        catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task LoadDataAsync_ItemOneSecondBeforeCutoff_IsExcluded()
    {
        // Arrange
        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        var timeProvider = new FakeTimeProvider(FrozenNow);
        var tile = new ProductInventoryCountTile(catalogRepositoryMock.Object, timeProvider);

        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, FrozenNow.UtcDateTime.AddDays(-30).AddSeconds(-1))
        };

        catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_ItemWithNullLastStockTaking_IsExcluded()
    {
        // Arrange
        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        var timeProvider = new FakeTimeProvider(FrozenNow);
        var tile = new ProductInventoryCountTile(catalogRepositoryMock.Object, timeProvider);

        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, null)
        };

        catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_CustomDaysOffset_ShiftsCutoff()
    {
        // Arrange
        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        var timeProvider = new FakeTimeProvider(FrozenNow);
        var tile = new CustomOffsetInventoryCountTile(catalogRepositoryMock.Object, timeProvider, daysOffset: 7);

        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, FrozenNow.UtcDateTime.AddDays(-10)), // excluded under 7-day window
            CreateItem("PROD002", ProductType.Product, FrozenNow.UtcDateTime.AddDays(-5))   // included under 7-day window
        };

        catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("count").GetInt32().Should().Be(1);
    }

    private CatalogAggregate CreateItem(string code, ProductType type, DateTime? lastStockTaking)
    {
        var item = new CatalogAggregate { ProductCode = code, Type = type };
        if (lastStockTaking.HasValue)
        {
            item.StockTakingHistory.Add(new StockTakingRecord { Date = lastStockTaking.Value });
        }
        return item;
    }

    private class CustomOffsetInventoryCountTile : InventoryCountTileBase
    {
        public CustomOffsetInventoryCountTile(ICatalogRepository repo, TimeProvider tp, int daysOffset) : base(repo, tp)
        {
            DaysOffset = daysOffset;
        }

        public override string Title => "Test";
        public override string Description => "Test";
        protected override Func<CatalogAggregate, bool> ItemFilter => c => true;
        protected override object GenerateDrillDownFilters() => new { };
    }
}
