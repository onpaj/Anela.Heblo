using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DashboardTiles;

public class MaterialExpirationSummaryTileTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly MaterialExpirationSummaryTile _tile;

    public MaterialExpirationSummaryTileTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _tile = new MaterialExpirationSummaryTile(_catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_ExpiredLot_CountsAsExpired()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today.AddDays(-1), amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("expired").GetInt32().Should().Be(1);
        data.GetProperty("within30").GetInt32().Should().Be(0);
        data.GetProperty("within90").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_LotExpiringToday_CountsAsWithin30()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today, amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("within30").GetInt32().Should().Be(1);
        data.GetProperty("expired").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_LotExpiringAtDay30_CountsAsWithin30()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today.AddDays(30), amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("within30").GetInt32().Should().Be(1);
        data.GetProperty("within90").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_LotExpiringAtDay31_CountsAsWithin90()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today.AddDays(31), amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("within90").GetInt32().Should().Be(1);
        data.GetProperty("within30").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_LotExpiringAtDay90_CountsAsWithin90()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today.AddDays(90), amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("within90").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task LoadDataAsync_LotBeyondHorizon_IsExcluded()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today.AddDays(91), amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_LotWithZeroAmount_IsExcluded()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001", CreateLot(today.AddDays(-1), amount: 0)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_LotWithNullExpiration_IsExcluded()
    {
        // Arrange
        SetupCatalog(CreateMaterial("MAT001", CreateLot(expiration: null, amount: 5)));

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_NonMaterialType_IsExcluded()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var product = CreateMaterial("PROD001", CreateLot(today.AddDays(-1), amount: 5));
        product.Type = ProductType.Product;
        SetupCatalog(product);

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_MaterialWithoutHasExpiration_IsExcluded()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var material = CreateMaterial("MAT001", CreateLot(today.AddDays(-1), amount: 5));
        material.HasExpiration = false;
        SetupCatalog(material);

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_MaterialWithLotsSpanningBuckets_CountsEachLotInItsBucket()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCatalog(CreateMaterial("MAT001",
            CreateLot(today.AddDays(-5), amount: 5),   // expired
            CreateLot(today.AddDays(10), amount: 5),   // within30
            CreateLot(today.AddDays(60), amount: 5),   // within90
            CreateLot(today.AddDays(200), amount: 5))); // beyond horizon

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("expired").GetInt32().Should().Be(1);
        data.GetProperty("within30").GetInt32().Should().Be(1);
        data.GetProperty("within90").GetInt32().Should().Be(1);
        data.GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task LoadDataAsync_EmptyRepository_ReturnsAllZeros()
    {
        // Arrange
        SetupCatalog();

        // Act
        var data = await LoadDataSection();

        // Assert
        data.GetProperty("expired").GetInt32().Should().Be(0);
        data.GetProperty("within30").GetInt32().Should().Be(0);
        data.GetProperty("within90").GetInt32().Should().Be(0);
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_HappyPath_ReturnsSuccessStatus()
    {
        // Arrange
        SetupCatalog();

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var doc = Serialize(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorStatus()
    {
        // Arrange
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var doc = Serialize(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetString().Should().Be("boom");
    }

    private async Task<JsonElement> LoadDataSection()
    {
        var result = await _tile.LoadDataAsync();
        var doc = Serialize(result);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static JsonDocument Serialize(object result)
    {
        var json = JsonSerializer.Serialize(result);
        return JsonDocument.Parse(json);
    }

    private void SetupCatalog(params CatalogAggregate[] items)
    {
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToList());
    }

    private static CatalogAggregate CreateMaterial(string code, params CatalogLot[] lots)
    {
        var item = new CatalogAggregate
        {
            ProductCode = code,
            Type = ProductType.Material,
            HasExpiration = true
        };
        item.Stock.Lots.AddRange(lots);
        return item;
    }

    private static CatalogLot CreateLot(DateOnly? expiration, decimal amount) => new()
    {
        Expiration = expiration,
        Amount = amount
    };
}
