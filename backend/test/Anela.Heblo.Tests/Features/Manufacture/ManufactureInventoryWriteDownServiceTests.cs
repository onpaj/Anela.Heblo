using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureInventoryWriteDownServiceTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ILogger<ManufactureInventoryWriteDownService>> _loggerMock;
    private readonly ManufactureInventoryWriteDownService _service;

    private const int ValidOrderId = 1;
    private const string TestUserName = "Test User";

    public ManufactureInventoryWriteDownServiceTests()
    {
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _loggerMock = new Mock<ILogger<ManufactureInventoryWriteDownService>>();

        _catalogRepositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>());

        _inventoryRepositoryMock
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ManufacturedProductInventoryItem> items, CancellationToken _) => items);

        _inventoryRepositoryMock
            .Setup(r => r.GetByProductCodesWithLogsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufacturedProductInventoryItem>());

        _service = new ManufactureInventoryWriteDownService(
            TimeProvider.System,
            _loggerMock.Object,
            _inventoryRepositoryMock.Object,
            _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task WriteDownAsync_CreatesInventoryItemsForFinishedProducts()
    {
        // Arrange
        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001",
                ProductName = "Product One",
                ActualQuantity = 10m,
                LotNumber = "LOT-A",
                ExpirationDate = new DateOnly(2027, 6, 1),
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-002",
                ProductName = "Product Two",
                ActualQuantity = 5m,
                LotNumber = null,
                ExpirationDate = null,
                ManufactureOrderId = ValidOrderId
            }
        };

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<ManufacturedProductInventoryItem>>(items =>
                    items.Any(i =>
                        i.ProductCode == "PROD-001" &&
                        i.Amount == 10m &&
                        i.ManufactureOrderId == ValidOrderId &&
                        i.CreatedBy == TestUserName) &&
                    items.Any(i =>
                        i.ProductCode == "PROD-002" &&
                        i.Amount == 5m &&
                        i.ManufactureOrderId == ValidOrderId &&
                        i.CreatedBy == TestUserName) &&
                    items.Count() == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteDownAsync_MergesIntoExistingRow_InsteadOfCreatingDuplicate()
    {
        // Arrange — an inventory row for the same product+lot+expiration already exists (from another order)
        var existing = new ManufacturedProductInventoryItem(
            productCode: "PROD-001",
            productName: "Product One",
            amount: 100m,
            createdBy: "Someone",
            createdAt: new DateTime(2027, 1, 1),
            lotNumber: "LOT-A",
            expirationDate: new DateOnly(2027, 6, 1),
            manufactureOrderId: 555);

        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001",
                ProductName = "Product One",
                ActualQuantity = 10m,
                LotNumber = "LOT-A",
                ExpirationDate = new DateOnly(2027, 6, 1),
                ManufactureOrderId = ValidOrderId
            }
        };

        _inventoryRepositoryMock
            .Setup(r => r.GetByProductCodesWithLogsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufacturedProductInventoryItem> { existing });

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert — existing row incremented, no new row inserted
        existing.Amount.Should().Be(110m);
        existing.WasWrittenDownByOrder(ValidOrderId).Should().BeTrue();
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteDownAsync_ReCompletingSameOrder_DoesNotWriteInventoryTwice()
    {
        // Arrange — the row already carries this order's write-down (order was completed before)
        var existing = new ManufacturedProductInventoryItem(
            productCode: "PROD-001",
            productName: "Product One",
            amount: 152m,
            createdBy: TestUserName,
            createdAt: new DateTime(2027, 1, 1),
            lotNumber: "LOT-A",
            expirationDate: new DateOnly(2027, 6, 1),
            manufactureOrderId: ValidOrderId);

        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001",
                ProductName = "Product One",
                ActualQuantity = 152m,
                LotNumber = "LOT-A",
                ExpirationDate = new DateOnly(2027, 6, 1),
                ManufactureOrderId = ValidOrderId
            }
        };

        _inventoryRepositoryMock
            .Setup(r => r.GetByProductCodesWithLogsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufacturedProductInventoryItem> { existing });

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert — amount unchanged, nothing added
        existing.Amount.Should().Be(152m);
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteDownAsync_AggregatesSameProductLotLinesIntoSingleRow()
    {
        // Arrange — two order lines for the same product+lot+expiration
        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001", ProductName = "Product One", ActualQuantity = 6m,
                LotNumber = "LOT-A", ExpirationDate = new DateOnly(2027, 6, 1), ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001", ProductName = "Product One", ActualQuantity = 4m,
                LotNumber = "LOT-A", ExpirationDate = new DateOnly(2027, 6, 1), ManufactureOrderId = ValidOrderId
            }
        };

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert — a single row with the summed amount
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<ManufacturedProductInventoryItem>>(items =>
                    items.Count() == 1 &&
                    items.Single().ProductCode == "PROD-001" &&
                    items.Single().Amount == 10m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteDownAsync_SkipsProductsWithZeroActualQuantity()
    {
        // Arrange
        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-ZERO",
                ProductName = "Zero Quantity",
                ActualQuantity = 0m,
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-NULL",
                ProductName = "Null Quantity",
                ActualQuantity = null,
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-OK",
                ProductName = "Has Quantity",
                ActualQuantity = 3m,
                ManufactureOrderId = ValidOrderId
            }
        };

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<ManufacturedProductInventoryItem>>(items =>
                    items.Single().ProductCode == "PROD-OK"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteDownAsync_ExcludesSemiProductsFromInventory()
    {
        // Arrange
        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "SEMI-001",
                ProductName = "Semi Product",
                ActualQuantity = 8m,
                ManufactureOrderId = ValidOrderId
            }
        };

        _catalogRepositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["SEMI-001"] = new CatalogAggregate { ProductCode = "SEMI-001", Type = ProductType.SemiProduct }
            });

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteDownAsync_IncludesOnlyNonSemiProductsWhenMixed()
    {
        // Arrange
        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "SEMI-001",
                ProductName = "Semi Product",
                ActualQuantity = 8m,
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001",
                ProductName = "Regular Product",
                ActualQuantity = 5m,
                ManufactureOrderId = ValidOrderId
            }
        };

        _catalogRepositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["SEMI-001"] = new CatalogAggregate { ProductCode = "SEMI-001", Type = ProductType.SemiProduct }
            });

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<ManufacturedProductInventoryItem>>(items =>
                    items.Single().ProductCode == "PROD-001"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteDownAsync_WhenAllProductsHaveZeroOrNullQuantity_DoesNotCallAddRangeAsync()
    {
        // Arrange
        var order = CreateOrder();
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-ZERO",
                ProductName = "Zero Quantity",
                ActualQuantity = 0m,
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-NULL",
                ProductName = "Null Quantity",
                ActualQuantity = null,
                ManufactureOrderId = ValidOrderId
            }
        };

        // Act
        await _service.WriteDownAsync(order, TestUserName, CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _catalogRepositoryMock.Verify(
            r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ManufactureOrder CreateOrder()
    {
        var order = new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            CreatedByUser = "Original User",
            ResponsiblePerson = "Test Person",
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7))
        };
        order.InitializeState(ManufactureOrderState.SemiProductManufactured, DateTime.UtcNow.AddDays(-1), "Original User");
        return order;
    }
}
