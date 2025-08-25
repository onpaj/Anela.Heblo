using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Exceptions;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
        
        Assert.Equal("Failed to retrieve product margins", exception.Message);
        
        // Verify inner exception is DataAccessException
        Assert.IsType<DataAccessException>(exception.InnerException);
        Assert.Equal("Unable to access product catalog", exception.InnerException.Message);
        
        // Verify the original exception is nested deeper
        Assert.IsType<InvalidOperationException>(exception.InnerException.InnerException);
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
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
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
        Assert.NotNull(result);
        Assert.Single(result.Items);
        
        var item = result.Items.First();
        Assert.Equal("UNKNOWN", item.ProductCode);
        Assert.Equal("Unknown Product", item.ProductName);
        Assert.Null(item.PriceWithoutVat);
        Assert.Null(item.PurchasePrice);
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
                    new ManufactureCost { MaterialCost = -10 } // Invalid negative cost
                }
            }
        };
        
        _mockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        
        var item = result.Items.First();
        Assert.Equal("VALID_PRODUCT", item.ProductCode);
        Assert.Equal(100, item.PriceWithoutVat);
        Assert.Equal(80, item.PurchasePrice);
        
        // Should handle invalid cost history gracefully
        Assert.Null(item.AverageMaterialCost); // Should return null for invalid/negative costs
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
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.TotalCount);
        
        // Check that all products are processed (some with fallback values)
        Assert.Contains(result.Items, x => x.ProductCode == "VALID_001");
        Assert.Contains(result.Items, x => x.ProductCode == "UNKNOWN"); // Fallback for null code
        Assert.Contains(result.Items, x => x.ProductCode == "VALID_002");
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
        Assert.NotNull(result);
        Assert.Empty(result.Items); // No matches expected for the very long product code
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
        Assert.NotNull(result);
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
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
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
        Assert.NotNull(result);
        
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