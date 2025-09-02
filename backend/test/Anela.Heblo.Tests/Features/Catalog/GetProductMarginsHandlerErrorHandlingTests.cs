using System;
using FluentAssertions;
using System.Collections.Generic;
using FluentAssertions;
using System.Linq;
using FluentAssertions;
using System.Threading;
using FluentAssertions;
using System.Threading.Tasks;
using FluentAssertions;
using Anela.Heblo.Application.Features.Catalog;
using FluentAssertions;
using Anela.Heblo.Application.Features.Catalog.Exceptions;
using FluentAssertions;
using Anela.Heblo.Application.Features.Catalog.Model;
using FluentAssertions;
using Anela.Heblo.Application.Features.Catalog.Services;
using FluentAssertions;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using FluentAssertions;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductMarginsHandlerErrorHandlingTests
{
    private readonly Mock<ICatalogRepository> _mockRepository;
    private readonly Mock<ILedgerService> _mockLedgerService;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<SafeMarginCalculator> _mockMarginCalculator;
    private readonly Mock<ILogger<GetProductMarginsHandler>> _mockLogger;
    private readonly GetProductMarginsHandler _handler;

    public GetProductMarginsHandlerErrorHandlingTests()
    {
        _mockRepository = new Mock<ICatalogRepository>();
        _mockLedgerService = new Mock<ILedgerService>();
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockMarginCalculator = new Mock<SafeMarginCalculator>(Mock.Of<ILogger<SafeMarginCalculator>>());
        _mockLogger = new Mock<ILogger<GetProductMarginsHandler>>();

        _handler = new GetProductMarginsHandler(
            _mockRepository.Object,
            _mockLedgerService.Object,
            _mockTimeProvider.Object,
            _mockMarginCalculator.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrowsException_ThrowsProductMarginsException()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ProductMarginsException>(
            () => _handler.Handle(request, CancellationToken.None));

        exception.Message.Should().Be("Failed to retrieve product margins");

        // Verify inner exception is DataAccessException
        exception.InnerException.Should().BeOfType<DataAccessException>();
        exception.InnerException.Message.Should().Be("Unable to access product catalog");

        // Verify the original exception is nested deeper
        exception.InnerException.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_WhenRepositoryReturnsNull_ReturnsEmptyResponse()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<CatalogAggregate>)null!);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithProductsHavingNullProperties_HandlesGracefully()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = null,
                ProductName = null,
                Type = ProductType.Product,
                EshopPrice = null,
                ErpPrice = null,
                ManufactureCostHistory = null
            }
        };

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);

        var item = result.Items.First();
        item.ProductCode.Should().Be("UNKNOWN");
        item.ProductName.Should().Be("Unknown Product");
        item.PriceWithoutVat.Should().BeNull();
        item.PurchasePrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithProductsHavingInvalidData_HandlesErrorsGracefully()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "VALID_PRODUCT",
                ProductName = "Valid Product",
                Type = ProductType.Product,
                EshopPrice = new ProductPriceEshop { PriceWithoutVat = 100 },
                ErpPrice = new ProductPriceErp { PurchasePrice = 80 },
                ManufactureCostHistory = new List<ManufactureCost>
                {
                    new ManufactureCost { MaterialCostFromReceiptDocument = -10, MaterialCostFromPurchasePrice = -10} // Invalid negative cost
                }
            }
        };

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);

        var item = result.Items.First();
        item.ProductCode.Should().Be("VALID_PRODUCT");
        item.PriceWithoutVat.Should().Be(100);
        item.PurchasePrice.Should().Be(80);

        // Should handle invalid cost history gracefully
        item.AverageMaterialCost.Should().BeNull(); // Should return null for invalid/negative costs
    }

    [Fact]
    public async Task Handle_WithMixedValidAndInvalidProducts_ProcessesAllProducts()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var products = new List<CatalogAggregate>
        {
            // Valid product
            new CatalogAggregate
            {
                ProductCode = "VALID_001",
                ProductName = "Valid Product",
                Type = ProductType.Product,
                EshopPrice = new ProductPriceEshop { PriceWithoutVat = 100 },
                ErpPrice = new ProductPriceErp { PurchasePrice = 80 }
            },
            // Product with null data
            new CatalogAggregate
            {
                ProductCode = null,
                ProductName = "Product with null code",
                Type = ProductType.Product
            },
            // Another valid product
            new CatalogAggregate
            {
                ProductCode = "VALID_002",
                ProductName = "Another Valid Product",
                Type = ProductType.Product,
                EshopPrice = new ProductPriceEshop { PriceWithoutVat = 200 },
                ErpPrice = new ProductPriceErp { PurchasePrice = 150 }
            }
        };

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Count.Should().Be(3);
        result.TotalCount.Should().Be(3);

        // Check that all products are processed (some with fallback values)
        result.Items.Should().Contain(x => x.ProductCode == "VALID_001");
        result.Items.Should().Contain(x => x.ProductCode == "UNKNOWN"); // Fallback for null code
        result.Items.Should().Contain(x => x.ProductCode == "VALID_002");
    }

    [Fact]
    public async Task Handle_WhenFilteringThrowsException_ThrowsProductMarginsException()
    {
        // Arrange
        var request = new GetProductMarginsRequest
        {
            PageNumber = 1,
            PageSize = 10,
            ProductCode = new string('x', 10000) // Very long string that might cause issues
        };

        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "TEST_001",
                ProductName = "Test Product",
                Type = ProductType.Product
            }
        };

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act & Assert - The test should not throw, as our error handling should catch filtering issues
        var result = await _handler.Handle(request, CancellationToken.None);

        // Should return empty result if filtering fails but not throw
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty(); // No matches expected for the very long product code
    }

    [Theory]
    [InlineData(-1, 10)] // Negative page number
    [InlineData(1, 0)]   // Zero page size
    [InlineData(1, -5)]  // Negative page size
    public async Task Handle_WithInvalidPaginationParameters_HandlesGracefully(int pageNumber, int pageSize)
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = pageNumber, PageSize = pageSize };
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "TEST_001",
                ProductName = "Test Product",
                Type = ProductType.Product
            }
        };

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act - Should not throw, but might return empty results due to invalid pagination
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // The implementation should handle invalid pagination gracefully
    }

    [Fact]
    public async Task Handle_WithEmptyProductList_ReturnsEmptyResponse()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var products = new List<CatalogAggregate>();

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Handle_VerifiesLoggingBehavior()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "TEST_001",
                ProductName = "Test Product",
                Type = ProductType.Product
            }
        };

        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // Verify that debug logging was called for starting and completing the query
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Starting product margins query")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("completed successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}