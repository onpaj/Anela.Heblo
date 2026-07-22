using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetMaterialsForPurchaseHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly GetMaterialsForPurchaseHandler _handler;

    public GetMaterialsForPurchaseHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _handler = new GetMaterialsForPurchaseHandler(_catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_SearchTermMatchesProductCodeOnly_ReturnsMatchingItem()
    {
        // Arrange
        var matching = CreateCatalogItem("MAT-ABC123", "Wax");
        var nonMatching = CreateCatalogItem("MAT-999", "Oil");
        var fixtures = new List<CatalogAggregate> { matching, nonMatching };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "ABC123", Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().ContainSingle();
        result.Materials.Single().ProductCode.Should().Be("MAT-ABC123");
    }

    [Fact]
    public async Task Handle_SearchTermMatchesProductNameOnly_ReturnsMatchingItem()
    {
        // Arrange
        var matching = CreateCatalogItem("MAT-001", "Beeswax Premium");
        var nonMatching = CreateCatalogItem("MAT-002", "Oil");
        var fixtures = new List<CatalogAggregate> { matching, nonMatching };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "Beeswax", Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().ContainSingle();
        result.Materials.Single().ProductCode.Should().Be("MAT-001");
    }

    [Fact]
    public async Task Handle_SearchTermMatchesBothFields_ReturnsItemExactlyOnce()
    {
        // Arrange
        var matching = CreateCatalogItem("MAT-WAX01", "Beeswax WAX01");
        var fixtures = new List<CatalogAggregate> { matching };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "WAX01", Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().HaveCount(1);
        result.Materials.Single().ProductCode.Should().Be("MAT-WAX01");
    }

    [Fact]
    public async Task Handle_SearchTermDifferentCase_StillMatches()
    {
        // Arrange
        var matching = CreateCatalogItem("mat-oil01", "olive oil");
        var fixtures = new List<CatalogAggregate> { matching };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "OLIVE", Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().ContainSingle();
        result.Materials.Single().ProductCode.Should().Be("mat-oil01");
    }

    [Fact]
    public async Task Handle_SearchTermMatchesNeitherField_ExcludesItem()
    {
        // Arrange
        var nonMatching = CreateCatalogItem("MAT-999", "Oil");
        var fixtures = new List<CatalogAggregate> { nonMatching };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "ZZZNOTFOUND", Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_NullEmptyOrWhitespaceSearchTerm_ReturnsAllEligibleItems(string? searchTerm)
    {
        // Arrange
        var fixtures = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT-001", "Beeswax"),
            CreateCatalogItem("MAT-002", "Olive Oil"),
            CreateCatalogItem("MAT-003", "Shea Butter"),
        };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = searchTerm, Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_NoPurchaseHistory_ReturnsNullLastPurchasePrice()
    {
        // Arrange
        var item = CreateCatalogItem("MAT-001", "Beeswax", purchaseHistory: new List<CatalogPurchaseRecord>());
        var fixtures = new List<CatalogAggregate> { item };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().ContainSingle();
        result.Materials.Single().LastPurchasePrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MultiplePurchaseHistoryRecords_ReturnsLastRecordPrice()
    {
        // Arrange
        var purchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord { Date = new DateTime(2024, 1, 1), PricePerPiece = 100m, Amount = 1, ProductCode = "MAT-001", SupplierName = "Supplier A" },
            new CatalogPurchaseRecord { Date = new DateTime(2024, 6, 1), PricePerPiece = 150m, Amount = 1, ProductCode = "MAT-001", SupplierName = "Supplier B" },
        };
        var item = CreateCatalogItem("MAT-001", "Beeswax", purchaseHistory: purchaseHistory);
        var fixtures = new List<CatalogAggregate> { item };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().ContainSingle();
        result.Materials.Single().LastPurchasePrice.Should().Be(150m);
    }

    [Fact]
    public async Task Handle_LimitExceedsMatchCount_ReturnsAllMatchingItems()
    {
        // Arrange
        var fixtures = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT-001", "Beeswax Special"),
            CreateCatalogItem("MAT-002", "Beeswax Regular"),
            CreateCatalogItem("MAT-003", "Olive Oil"),
        };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "Beeswax", Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().HaveCount(2);
        result.Materials.Select(m => m.ProductCode).Should().BeEquivalentTo("MAT-001", "MAT-002");
    }

    [Fact]
    public async Task Handle_LimitBelowMatchCount_ReturnsExactlyLimitMatchingItems()
    {
        // Arrange
        var fixtures = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT-001", "Beeswax A"),
            CreateCatalogItem("MAT-002", "Beeswax B"),
            CreateCatalogItem("MAT-003", "Beeswax C"),
            CreateCatalogItem("MAT-004", "Olive Oil"),
        };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = "Beeswax", Limit = 2 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — exactly Limit items, and every one of them is a genuine match
        // (proves truncation happens on the filtered set, not before filtering)
        result.Materials.Should().HaveCount(2);
        result.Materials.Select(m => m.ProductCode)
            .Should().OnlyContain(code => new[] { "MAT-001", "MAT-002", "MAT-003" }.Contains(code));
    }

    [Fact]
    public async Task Handle_NoSearchTermWithLimit_ReturnsExactlyLimitItems()
    {
        // Arrange
        var fixtures = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT-001", "Item A"),
            CreateCatalogItem("MAT-002", "Item B"),
            CreateCatalogItem("MAT-003", "Item C"),
        };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { SearchTerm = null, Limit = 2 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ResultsOrderedByProductNameAlphabetically()
    {
        // Arrange
        var fixtures = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT-001", "Zinc Oxide"),
            CreateCatalogItem("MAT-002", "Almond Oil"),
            CreateCatalogItem("MAT-003", "Mango Butter"),
        };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Select(m => m.ProductName).Should().ContainInOrder("Almond Oil", "Mango Butter", "Zinc Oxide");
    }

    [Fact]
    public async Task Handle_MapsProductTypeToStringRepresentation()
    {
        // Arrange
        var fixtures = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT-001", "Beeswax", type: ProductType.Material),
            CreateCatalogItem("GDS-001", "Gift Box", type: ProductType.Goods),
        };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Materials.Single(m => m.ProductCode == "MAT-001").ProductType.Should().Be("Material");
        result.Materials.Single(m => m.ProductCode == "GDS-001").ProductType.Should().Be("Goods");
    }

    [Fact]
    public async Task Handle_EmptyLocationAndMinimalOrderQuantity_MapToNull()
    {
        // Arrange
        var item = CreateCatalogItem("MAT-001", "Beeswax", location: "", minimalOrderQuantity: "");
        var fixtures = new List<CatalogAggregate> { item };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var dto = result.Materials.Single();
        dto.Location.Should().BeNull();
        dto.MinimalOrderQuantity.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonEmptyLocationAndMinimalOrderQuantity_PassThroughUnchangedWithStock()
    {
        // Arrange
        var item = CreateCatalogItem("MAT-001", "Beeswax", availableStock: 42m, location: "A-12-3", minimalOrderQuantity: "10 kg");
        var fixtures = new List<CatalogAggregate> { item };

        _catalogRepositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures);

        var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var dto = result.Materials.Single();
        dto.Location.Should().Be("A-12-3");
        dto.MinimalOrderQuantity.Should().Be("10 kg");
        dto.CurrentStock.Should().Be(42);
    }

    private static CatalogAggregate CreateCatalogItem(
        string productCode,
        string productName,
        ProductType type = ProductType.Material,
        decimal availableStock = 10m,
        string location = "",
        List<CatalogPurchaseRecord>? purchaseHistory = null,
        string minimalOrderQuantity = "")
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Type = type,
            Stock = new StockData { Erp = availableStock },
            Location = location,
            PurchaseHistory = purchaseHistory ?? new List<CatalogPurchaseRecord>(),
            MinimalOrderQuantity = minimalOrderQuantity
        };
    }
}
