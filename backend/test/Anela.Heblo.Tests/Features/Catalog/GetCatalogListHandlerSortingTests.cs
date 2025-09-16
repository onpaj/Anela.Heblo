using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogList;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Tests for the complex LastInventoryDays sorting logic in GetCatalogListHandler
/// </summary>
public class GetCatalogListHandlerSortingTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetCatalogListHandler _handler;

    public GetCatalogListHandlerSortingTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetCatalogListHandler(_catalogRepositoryMock.Object, _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_LastInventoryDaysDescending_ItemsWithoutInventoryFirst()
    {
        // Arrange - Test the complex sorting rule:
        // Descending: Items WITHOUT inventory first, then items WITH inventory by oldest first (ascending = biggest days)
        var today = DateTime.Today;
        var items = new List<CatalogAggregate>
        {
            CreateCatalogItem("ITEM1", "Item 1", "LocationA", today.AddDays(-5)),   // 5 days ago
            CreateCatalogItem("ITEM2", "Item 2", "LocationC", null),                // No inventory
            CreateCatalogItem("ITEM3", "Item 3", "LocationB", today.AddDays(-10)),  // 10 days ago  
            CreateCatalogItem("ITEM4", "Item 4", "LocationD", null),                // No inventory
            CreateCatalogItem("ITEM5", "Item 5", "LocationE", today.AddDays(-1))    // 1 day ago
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "lastinventorydays",
            SortDescending = true
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var expectedDtos = new List<CatalogItemDto>
        {
            new() { ProductCode = "ITEM2", Location = "LocationC" },  // No inventory, LocationC
            new() { ProductCode = "ITEM4", Location = "LocationD" },  // No inventory, LocationD  
            new() { ProductCode = "ITEM3" },  // 10 days ago (oldest = biggest days)
            new() { ProductCode = "ITEM1" },  // 5 days ago
            new() { ProductCode = "ITEM5" }   // 1 day ago (newest = smallest days)
        };

        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.Items.Count);

        // Verify items without inventory come first (sorted by location)
        Assert.Equal("ITEM2", result.Items[0].ProductCode); // LocationC
        Assert.Equal("ITEM4", result.Items[1].ProductCode); // LocationD

        // Verify items with inventory are sorted oldest first (biggest days = descending logic)
        Assert.Equal("ITEM3", result.Items[2].ProductCode); // 10 days ago (oldest)
        Assert.Equal("ITEM1", result.Items[3].ProductCode); // 5 days ago
        Assert.Equal("ITEM5", result.Items[4].ProductCode); // 1 day ago (newest)
    }

    [Fact]
    public async Task Handle_LastInventoryDaysAscending_ItemsWithInventoryFirst()
    {
        // Arrange - Test the ascending rule:
        // Ascending: Items WITH inventory by newest first (descending = smallest days), then items WITHOUT inventory
        var today = DateTime.Today;
        var items = new List<CatalogAggregate>
        {
            CreateCatalogItem("ITEM1", "Item 1", "LocationA", today.AddDays(-5)),   // 5 days ago
            CreateCatalogItem("ITEM2", "Item 2", "LocationC", null),                // No inventory
            CreateCatalogItem("ITEM3", "Item 3", "LocationB", today.AddDays(-10)),  // 10 days ago  
            CreateCatalogItem("ITEM4", "Item 4", "LocationD", null),                // No inventory
            CreateCatalogItem("ITEM5", "Item 5", "LocationE", today.AddDays(-1))    // 1 day ago
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "lastinventorydays",
            SortDescending = false
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var expectedDtos = new List<CatalogItemDto>
        {
            new() { ProductCode = "ITEM5" },  // 1 day ago (newest = smallest days)
            new() { ProductCode = "ITEM1" },  // 5 days ago
            new() { ProductCode = "ITEM3" },  // 10 days ago (oldest)
            new() { ProductCode = "ITEM2", Location = "LocationC" },  // No inventory, LocationC
            new() { ProductCode = "ITEM4", Location = "LocationD" }   // No inventory, LocationD
        };

        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.Items.Count);

        // Verify items with inventory come first (newest first = smallest days = ascending logic)
        Assert.Equal("ITEM5", result.Items[0].ProductCode); // 1 day ago (newest)
        Assert.Equal("ITEM1", result.Items[1].ProductCode); // 5 days ago
        Assert.Equal("ITEM3", result.Items[2].ProductCode); // 10 days ago (oldest)

        // Verify items without inventory come last (sorted by location)
        Assert.Equal("ITEM2", result.Items[3].ProductCode); // LocationC
        Assert.Equal("ITEM4", result.Items[4].ProductCode); // LocationD
    }

    [Fact]
    public async Task Handle_LastInventoryDaysDescending_OnlyItemsWithoutInventory_SortedByLocation()
    {
        // Arrange
        var items = new List<CatalogAggregate>
        {
            CreateCatalogItem("ITEM1", "Item 1", "LocationZ", null),
            CreateCatalogItem("ITEM2", "Item 2", "LocationA", null),
            CreateCatalogItem("ITEM3", "Item 3", "LocationM", null)
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "lastinventorydays",
            SortDescending = true
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var expectedDtos = new List<CatalogItemDto>
        {
            new() { ProductCode = "ITEM2", Location = "LocationA" },  // LocationA first
            new() { ProductCode = "ITEM3", Location = "LocationM" },  // LocationM second
            new() { ProductCode = "ITEM1", Location = "LocationZ" }   // LocationZ last
        };

        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count);

        // Verify items are sorted by location alphabetically
        Assert.Equal("ITEM2", result.Items[0].ProductCode); // LocationA
        Assert.Equal("ITEM3", result.Items[1].ProductCode); // LocationM  
        Assert.Equal("ITEM1", result.Items[2].ProductCode); // LocationZ
    }

    [Fact]
    public async Task Handle_LastInventoryDaysDescending_OnlyItemsWithInventory_SortedByDateAscending()
    {
        // Arrange - Only items with inventory, should be sorted oldest first (biggest days)
        var today = DateTime.Today;
        var items = new List<CatalogAggregate>
        {
            CreateCatalogItem("ITEM1", "Item 1", "LocationA", today.AddDays(-5)),   // 5 days ago
            CreateCatalogItem("ITEM2", "Item 2", "LocationB", today.AddDays(-15)),  // 15 days ago (oldest)
            CreateCatalogItem("ITEM3", "Item 3", "LocationC", today.AddDays(-2))    // 2 days ago (newest)
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "lastinventorydays",
            SortDescending = true
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var expectedDtos = new List<CatalogItemDto>
        {
            new() { ProductCode = "ITEM2" },  // 15 days ago (oldest = biggest days)
            new() { ProductCode = "ITEM1" },  // 5 days ago
            new() { ProductCode = "ITEM3" }   // 2 days ago (newest = smallest days)
        };

        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count);

        // Verify descending = oldest first (biggest days first)
        Assert.Equal("ITEM2", result.Items[0].ProductCode); // 15 days ago (oldest)
        Assert.Equal("ITEM1", result.Items[1].ProductCode); // 5 days ago
        Assert.Equal("ITEM3", result.Items[2].ProductCode); // 2 days ago (newest)
    }

    [Fact]
    public async Task Handle_LastInventoryDaysAscending_OnlyItemsWithInventory_SortedByDateDescending()
    {
        // Arrange - Only items with inventory, should be sorted newest first (smallest days)
        var today = DateTime.Today;
        var items = new List<CatalogAggregate>
        {
            CreateCatalogItem("ITEM1", "Item 1", "LocationA", today.AddDays(-5)),   // 5 days ago
            CreateCatalogItem("ITEM2", "Item 2", "LocationB", today.AddDays(-15)),  // 15 days ago (oldest)
            CreateCatalogItem("ITEM3", "Item 3", "LocationC", today.AddDays(-2))    // 2 days ago (newest)
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "lastinventorydays",
            SortDescending = false
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var expectedDtos = new List<CatalogItemDto>
        {
            new() { ProductCode = "ITEM3" },  // 2 days ago (newest = smallest days)
            new() { ProductCode = "ITEM1" },  // 5 days ago
            new() { ProductCode = "ITEM2" }   // 15 days ago (oldest = biggest days)
        };

        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count);

        // Verify ascending = newest first (smallest days first)
        Assert.Equal("ITEM3", result.Items[0].ProductCode); // 2 days ago (newest)
        Assert.Equal("ITEM1", result.Items[1].ProductCode); // 5 days ago
        Assert.Equal("ITEM2", result.Items[2].ProductCode); // 15 days ago (oldest)
    }

    [Fact]
    public async Task Handle_StandardSorting_WorksCorrectly()
    {
        // Arrange - Test that standard sorting (not lastinventorydays) still works
        var items = new List<CatalogAggregate>
        {
            CreateCatalogItem("ITEM-C", "Item C", "LocationA", null),
            CreateCatalogItem("ITEM-A", "Item A", "LocationB", null),
            CreateCatalogItem("ITEM-B", "Item B", "LocationC", null)
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "productcode",
            SortDescending = false
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var expectedDtos = new List<CatalogItemDto>
        {
            new() { ProductCode = "ITEM-A" },
            new() { ProductCode = "ITEM-B" },
            new() { ProductCode = "ITEM-C" }
        };

        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count);

        // Verify standard ascending sort by ProductCode
        Assert.Equal("ITEM-A", result.Items[0].ProductCode);
        Assert.Equal("ITEM-B", result.Items[1].ProductCode);
        Assert.Equal("ITEM-C", result.Items[2].ProductCode);
    }

    private static CatalogAggregate CreateCatalogItem(string productCode, string productName, string location, DateTime? lastStockTaking)
    {
        var item = new CatalogAggregate
        {
            Id = productCode,
            ProductName = productName,
            Type = ProductType.Product,
            Location = location
        };

        if (lastStockTaking.HasValue)
        {
            // Add a stock taking record to simulate last stock taking
            item.StockTakingHistory = new List<StockTakingRecord>
            {
                new StockTakingRecord
                {
                    Date = lastStockTaking.Value,
                    Code = productCode,
                    AmountOld = 0,
                    AmountNew = 10
                }
            };
        }

        return item;
    }
}