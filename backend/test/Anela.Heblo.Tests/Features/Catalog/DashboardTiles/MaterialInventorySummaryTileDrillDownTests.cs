using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DashboardTiles;

/// <summary>
/// The material inventory tiles must drill down to the same subset they count,
/// not to every material.
/// </summary>
public class MaterialInventorySummaryTileDrillDownTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();

    public MaterialInventorySummaryTileDrillDownTests()
    {
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());
    }

    [Fact]
    public async Task WithoutExpirationTile_DrillDown_FiltersMaterialsWithoutExpiration()
    {
        var tile = new MaterialWithoutExpirationInventorySummaryTile(_catalogRepositoryMock.Object);

        var filters = await LoadDrillDownFiltersAsync(tile);

        filters.GetProperty("type").GetString().Should().Be("Material");
        filters.GetProperty("hasExpiration").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task WithExpirationTile_DrillDown_FiltersMaterialsWithExpiration()
    {
        var tile = new MaterialWithExpirationInventorySummaryTile(_catalogRepositoryMock.Object);

        var filters = await LoadDrillDownFiltersAsync(tile);

        filters.GetProperty("type").GetString().Should().Be("Material");
        filters.GetProperty("hasExpiration").GetBoolean().Should().BeTrue();
    }

    private static async Task<JsonElement> LoadDrillDownFiltersAsync(InventorySummaryTileBase tile)
    {
        var result = await tile.LoadDataAsync();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return doc.RootElement.GetProperty("drillDown").GetProperty("filters").Clone();
    }
}
