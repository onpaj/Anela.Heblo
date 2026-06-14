using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.UseCases.GetManufactureStockTakingHistory;

public class GetManufactureStockTakingHistoryHandlerTests
{
    private readonly Mock<IManufactureCatalogSource> _catalogSourceMock;
    private readonly GetManufactureStockTakingHistoryHandler _handler;

    public GetManufactureStockTakingHistoryHandlerTests()
    {
        _catalogSourceMock = new Mock<IManufactureCatalogSource>();
        _handler = new GetManufactureStockTakingHistoryHandler(_catalogSourceMock.Object);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetManufactureStockTakingHistoryRequest
        {
            ProductCode = "NONEXISTENT",
            PageNumber = 1,
            PageSize = 20
        };

        _catalogSourceMock
            .Setup(s => s.GetByIdAsync("NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ProductNotFound);
        result.Params.Should().NotBeNull();
        result.Params!["ProductCode"].Should().Be("NONEXISTENT");
    }

    [Fact]
    public async Task Handle_ValidProduct_DefaultSort_ReturnsPaginatedHistoryNewestFirst()
    {
        // Arrange
        const string productCode = "TEST-PRODUCT";
        var request = new GetManufactureStockTakingHistoryRequest
        {
            ProductCode = productCode,
            PageNumber = 1,
            PageSize = 3,
        };

        var today = new DateTime(2026, 6, 12);
        var history = new List<StockTakingRecord>
        {
            new() { Id = 1, Date = today.AddDays(-5), AmountOld = 0,  AmountNew = 5,  User = "u1", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 2, Date = today.AddDays(-4), AmountOld = 5,  AmountNew = 12, User = "u3", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 3, Date = today.AddDays(-3), AmountOld = 12, AmountNew = 8,  User = "u1", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 4, Date = today.AddDays(-2), AmountOld = 8,  AmountNew = 10, User = "u2", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 5, Date = today.AddDays(-1), AmountOld = 10, AmountNew = 15, User = "u1", Code = productCode, Type = StockTakingType.Eshop },
        };

        var product = new CatalogAggregate
        {
            Id = productCode,
            ProductName = "Test Product",
            Type = ProductType.Material,
            StockTakingHistory = history,
        };

        _catalogSourceMock
            .Setup(s => s.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.TotalPages.Should().Be(2); // ceil(5/3)
        result.Items.Should().HaveCount(3);
        result.Items.Select(i => i.Id).Should().ContainInOrder(5, 4, 3); // newest first
        result.Items.First().Difference.Should().Be(5); // 15 - 10
    }

    [Theory]
    [InlineData("date", true, new[] { 5, 4, 3, 2, 1 })]
    [InlineData("date", false, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("code", true, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("code", false, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("type", true, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("type", false, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("amountnew", true, new[] { 5, 2, 4, 3, 1 })]
    [InlineData("amountnew", false, new[] { 1, 3, 4, 2, 5 })]
    [InlineData("amountold", true, new[] { 3, 5, 4, 2, 1 })]
    [InlineData("amountold", false, new[] { 1, 2, 4, 5, 3 })]
    [InlineData("user", true, new[] { 2, 4, 1, 3, 5 })]
    [InlineData(null, true, new[] { 5, 4, 3, 2, 1 })]
    [InlineData("unknown", true, new[] { 5, 4, 3, 2, 1 })]
    public async Task Handle_SortBranches_HonourSortByAndSortDescending(string? sortBy, bool descending, int[] expectedIdOrder)
    {
        // Arrange
        const string productCode = "TEST-PRODUCT";
        var today = new DateTime(2026, 6, 12);
        var history = new List<StockTakingRecord>
        {
            new() { Id = 1, Date = today.AddDays(-5), AmountOld = 0,  AmountNew = 5,  User = "u1", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 2, Date = today.AddDays(-4), AmountOld = 5,  AmountNew = 12, User = "u3", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 3, Date = today.AddDays(-3), AmountOld = 12, AmountNew = 8,  User = "u1", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 4, Date = today.AddDays(-2), AmountOld = 8,  AmountNew = 10, User = "u2", Code = productCode, Type = StockTakingType.Eshop },
            new() { Id = 5, Date = today.AddDays(-1), AmountOld = 10, AmountNew = 15, User = "u1", Code = productCode, Type = StockTakingType.Eshop },
        };
        var product = new CatalogAggregate
        {
            Id = productCode,
            ProductName = "Test Product",
            Type = ProductType.Material,
            StockTakingHistory = history,
        };
        _catalogSourceMock
            .Setup(s => s.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var request = new GetManufactureStockTakingHistoryRequest
        {
            ProductCode = productCode,
            PageNumber = 1,
            PageSize = 10,
            SortBy = sortBy,
            SortDescending = descending,
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Select(i => i.Id).Should().ContainInOrder(expectedIdOrder);
    }
}
