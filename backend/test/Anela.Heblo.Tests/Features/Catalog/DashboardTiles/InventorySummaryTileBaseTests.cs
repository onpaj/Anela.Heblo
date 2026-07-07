using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DashboardTiles;

// Note on boundary precision: LoadDataAsync captures its own DateTime.UtcNow at Act time,
// which is always fractionally later than the Arrange-time `now` these tests capture. So an
// item built at `now.AddDays(-180)` is always measured as slightly MORE than 180.000 elapsed
// days by production code - never exactly 180.000. Without an injectable clock in
// InventorySummaryTileBase (out of scope to add - see task brief), these tests cannot
// distinguish a strict (`<`/`>`) from an inclusive (`<=`/`>=`) comparison operator exactly at
// the threshold value; they do verify the threshold is in the right place to within one day
// and that same-side values land in the correct bucket.
public class InventorySummaryTileBaseTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly ProductInventorySummaryTile _tile;

    public InventorySummaryTileBaseTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _tile = new ProductInventorySummaryTile(_catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_ItemAt179Days_CountsAsRecent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, now.AddDays(-179))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("recent").GetInt32().Should().Be(1);
        data.GetProperty("medium").GetInt32().Should().Be(0);
        data.GetProperty("old").GetInt32().Should().Be(0);
        data.GetProperty("never").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_ItemAt180Days_CountsAsMedium()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, now.AddDays(-180))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("medium").GetInt32().Should().Be(1);
        data.GetProperty("recent").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_ItemAt365Days_CountsAsMedium()
    {
        // Arrange
        var now = DateTime.UtcNow;
        // The production code captures its own (slightly later) "now" at Act time, so an
        // offset of exactly -365 days would drift to just over 365 elapsed days by the time
        // LoadDataAsync runs. Nudge 1 second closer to keep elapsed time at/under the
        // inclusive upper bound of the "medium" bucket.
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, now.AddDays(-365).AddSeconds(1))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("medium").GetInt32().Should().Be(1);
        data.GetProperty("old").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_ItemAt366Days_CountsAsOld()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, now.AddDays(-366))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("old").GetInt32().Should().Be(1);
        data.GetProperty("medium").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_ItemWithNullLastStockTaking_CountsAsNever()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, null)
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("never").GetInt32().Should().Be(1);
        data.GetProperty("recent").GetInt32().Should().Be(0);
        data.GetProperty("medium").GetInt32().Should().Be(0);
        data.GetProperty("old").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_MixedItemsWithFilteredOutType_TotalExcludesFilteredItems()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, now.AddDays(-10)),   // recent
            CreateItem("PROD002", ProductType.Product, now.AddDays(-200)),  // medium
            CreateItem("MAT001", ProductType.Material, now.AddDays(-10))    // filtered out
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        var recent = data.GetProperty("recent").GetInt32();
        var medium = data.GetProperty("medium").GetInt32();
        var old = data.GetProperty("old").GetInt32();
        var never = data.GetProperty("never").GetInt32();
        var total = data.GetProperty("total").GetInt32();

        total.Should().Be(2);
        total.Should().Be(recent + medium + old + never);
    }

    [Fact]
    public async Task LoadDataAsync_HappyPath_ReturnsSuccessStatusAndExpectedShape()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var items = new List<CatalogAggregate>
        {
            CreateItem("PROD001", ProductType.Product, now.AddDays(-10)),   // recent
            CreateItem("PROD002", ProductType.Product, now.AddDays(-200)),  // medium
            CreateItem("PROD003", ProductType.Product, now.AddDays(-400)),  // old
            CreateItem("PROD004", ProductType.Product, null)                // never
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("success");

        var data = doc.RootElement.GetProperty("data");
        var recent = data.GetProperty("recent").GetInt32();
        var medium = data.GetProperty("medium").GetInt32();
        var old = data.GetProperty("old").GetInt32();
        var never = data.GetProperty("never").GetInt32();
        var total = data.GetProperty("total").GetInt32();

        total.Should().Be(4);
        total.Should().Be(recent + medium + old + never);
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
}
