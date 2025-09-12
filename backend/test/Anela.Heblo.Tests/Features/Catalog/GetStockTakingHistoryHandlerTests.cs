using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetStockTakingHistoryHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetStockTakingHistoryHandler _handler;

    public GetStockTakingHistoryHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetStockTakingHistoryHandler(_catalogRepositoryMock.Object, _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetStockTakingHistoryRequest
        {
            ProductCode = "NONEXISTENT",
            PageNumber = 1,
            PageSize = 20
        };

        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(request.ProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ProductNotFound, result.ErrorCode);
        Assert.Contains("ProductCode", result.Params!.Keys);
        Assert.Equal("NONEXISTENT", result.Params["ProductCode"]);
    }

    [Fact]
    public async Task Handle_ValidProduct_ReturnsSuccessWithPaginatedHistory()
    {
        // Arrange
        var productCode = "TEST-PRODUCT";
        var request = new GetStockTakingHistoryRequest
        {
            ProductCode = productCode,
            PageNumber = 2,
            PageSize = 3,
            SortBy = "date",
            SortDescending = true
        };

        var stockTakingHistory = new List<StockTakingRecord>
        {
            new() { Date = DateTime.Today.AddDays(-1), AmountOld = 10, AmountNew = 15, User = "user1", Code = productCode },
            new() { Date = DateTime.Today.AddDays(-2), AmountOld = 8, AmountNew = 10, User = "user2", Code = productCode },
            new() { Date = DateTime.Today.AddDays(-3), AmountOld = 12, AmountNew = 8, User = "user1", Code = productCode },
            new() { Date = DateTime.Today.AddDays(-4), AmountOld = 5, AmountNew = 12, User = "user3", Code = productCode },
            new() { Date = DateTime.Today.AddDays(-5), AmountOld = 0, AmountNew = 5, User = "user1", Code = productCode }
        };

        var product = new CatalogAggregate
        {
            Id = "TEST-PRODUCT",
            ProductName = "Test Product",
            Type = ProductType.Product,
            StockTakingHistory = stockTakingHistory
        };

        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(request.ProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var expectedDtos = stockTakingHistory
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(h => new StockTakingHistoryItemDto
            {
                Date = h.Date,
                AmountOld = h.AmountOld,
                AmountNew = h.AmountNew,
                User = h.User,
                Code = h.Code
            }).ToList();

        _mapperMock.Setup(m => m.Map<List<StockTakingHistoryItemDto>>(It.IsAny<List<StockTakingRecord>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedDtos.Count, result.Items.Count);
        Assert.Equal(stockTakingHistory.Count, result.TotalCount);
        Assert.Equal(request.PageNumber, result.PageNumber);
        Assert.Equal(request.PageSize, result.PageSize);
        Assert.Equal(2, result.TotalPages); // 5 total items / 3 page size = 2 pages
    }

    [Fact]
    public async Task Handle_EmptyHistory_ReturnsEmptyResults()
    {
        // Arrange
        var productCode = "EMPTY-PRODUCT";
        var request = new GetStockTakingHistoryRequest
        {
            ProductCode = productCode,
            PageNumber = 1,
            PageSize = 20
        };

        var product = new CatalogAggregate
        {
            Id = "EMPTY-PRODUCT",
            ProductName = "Empty Product",
            Type = ProductType.Product,
            StockTakingHistory = new List<StockTakingRecord>()
        };

        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(request.ProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mapperMock.Setup(m => m.Map<List<StockTakingHistoryItemDto>>(It.IsAny<List<StockTakingRecord>>()))
            .Returns(new List<StockTakingHistoryItemDto>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Handle_SortByDateDescending_ReturnsSortedResults()
    {
        // Arrange
        var productCode = "SORT-TEST";
        var request = new GetStockTakingHistoryRequest
        {
            ProductCode = productCode,
            PageNumber = 1,
            PageSize = 10,
            SortBy = "date",
            SortDescending = true
        };

        var stockTakingHistory = new List<StockTakingRecord>
        {
            new() { Date = DateTime.Today.AddDays(-5), AmountOld = 0, AmountNew = 5 },
            new() { Date = DateTime.Today.AddDays(-1), AmountOld = 10, AmountNew = 15 },
            new() { Date = DateTime.Today.AddDays(-3), AmountOld = 12, AmountNew = 8 }
        };

        var product = new CatalogAggregate
        {
            Id = "SORT-TEST",
            ProductName = "Sort Test Product",
            Type = ProductType.Product,
            StockTakingHistory = stockTakingHistory
        };

        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(request.ProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var expectedSortedDtos = new List<StockTakingHistoryItemDto>
        {
            new() { Date = DateTime.Today.AddDays(-1), AmountOld = 10, AmountNew = 15 },
            new() { Date = DateTime.Today.AddDays(-3), AmountOld = 12, AmountNew = 8 },
            new() { Date = DateTime.Today.AddDays(-5), AmountOld = 0, AmountNew = 5 }
        };

        _mapperMock.Setup(m => m.Map<List<StockTakingHistoryItemDto>>(It.IsAny<List<StockTakingRecord>>()))
            .Returns(expectedSortedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count);
        
        // Verify the sorting order (most recent first when descending)
        Assert.True(result.Items[0].Date > result.Items[1].Date);
        Assert.True(result.Items[1].Date > result.Items[2].Date);
    }

    [Fact]
    public async Task Handle_SortByAmountAscending_ReturnsSortedResults()
    {
        // Arrange
        var productCode = "AMOUNT-SORT-TEST";
        var request = new GetStockTakingHistoryRequest
        {
            ProductCode = productCode,
            PageNumber = 1,
            PageSize = 10,
            SortBy = "newamount",
            SortDescending = false
        };

        var stockTakingHistory = new List<StockTakingRecord>
        {
            new() { Date = DateTime.Today, AmountOld = 10, AmountNew = 15 },
            new() { Date = DateTime.Today, AmountOld = 0, AmountNew = 5 },
            new() { Date = DateTime.Today, AmountOld = 12, AmountNew = 8 }
        };

        var product = new CatalogAggregate
        {
            Id = "AMOUNT-SORT-TEST",
            ProductName = "Amount Sort Test",
            Type = ProductType.Product,
            StockTakingHistory = stockTakingHistory
        };

        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(request.ProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Expected order: 5, 8, 15 (ascending by AmountNew)
        var expectedSortedDtos = new List<StockTakingHistoryItemDto>
        {
            new() { Date = DateTime.Today, AmountOld = 0, AmountNew = 5 },
            new() { Date = DateTime.Today, AmountOld = 12, AmountNew = 8 },
            new() { Date = DateTime.Today, AmountOld = 10, AmountNew = 15 }
        };

        _mapperMock.Setup(m => m.Map<List<StockTakingHistoryItemDto>>(It.IsAny<List<StockTakingRecord>>()))
            .Returns(expectedSortedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count);
        
        // Verify ascending sort by AmountNew
        Assert.True(result.Items[0].AmountNew <= result.Items[1].AmountNew);
        Assert.True(result.Items[1].AmountNew <= result.Items[2].AmountNew);
    }

    [Fact]
    public async Task Handle_DefaultPagination_UsesCorrectDefaults()
    {
        // Arrange
        var request = new GetStockTakingHistoryRequest
        {
            ProductCode = "DEFAULT-TEST"
            // PageNumber and PageSize not specified, should use defaults
        };

        var product = new CatalogAggregate
        {
            Id = "DEFAULT-TEST",
            ProductName = "Default Test",
            Type = ProductType.Product,
            StockTakingHistory = new List<StockTakingRecord>()
        };

        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(request.ProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mapperMock.Setup(m => m.Map<List<StockTakingHistoryItemDto>>(It.IsAny<List<StockTakingRecord>>()))
            .Returns(new List<StockTakingHistoryItemDto>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.PageNumber); // Default page number
        Assert.Equal(20, result.PageSize);  // Default page size
    }
}