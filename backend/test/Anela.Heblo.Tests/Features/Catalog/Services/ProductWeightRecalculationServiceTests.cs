using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

public class ProductWeightRecalculationServiceTests
{
    private readonly Mock<ICatalogRepository> _mockCatalogRepository;
    private readonly Mock<IProductWeightClient> _mockProductWeightClient;
    private readonly Mock<ILogger<ProductWeightRecalculationService>> _mockLogger;
    private readonly ProductWeightRecalculationService _service;

    public ProductWeightRecalculationServiceTests()
    {
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _mockProductWeightClient = new Mock<IProductWeightClient>();
        _mockLogger = new Mock<ILogger<ProductWeightRecalculationService>>();
        
        _service = new ProductWeightRecalculationService(
            _mockCatalogRepository.Object,
            _mockProductWeightClient.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithValidProducts_ProcessesAllProducts()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProduct("PROD001"),
            CreateProduct("PROD002"),
            CreateProduct("PROD003")
        };

        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1.5);

        // Act
        await _service.RecalculateAllProductWeights();

        // Assert
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("PROD001", It.IsAny<CancellationToken>()), 
            Times.Once);
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("PROD002", It.IsAny<CancellationToken>()), 
            Times.Once);
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("PROD003", It.IsAny<CancellationToken>()), 
            Times.Once);

        VerifyInfoLog("Starting product weight recalculation for all products");
        VerifyInfoLog("Product weight recalculation completed. Success: 3, Errors: 0");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithMixedProductTypes_ProcessesOnlyProducts()
    {
        // Arrange
        var catalogItems = new List<CatalogAggregate>
        {
            CreateProduct("PROD001"),
            CreateMaterial("MAT001"),
            CreateProduct("PROD002"),
            CreateSemiProduct("SEMI001")
        };

        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2.0);

        // Act
        await _service.RecalculateAllProductWeights();

        // Assert
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("PROD001", It.IsAny<CancellationToken>()), 
            Times.Once);
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("PROD002", It.IsAny<CancellationToken>()), 
            Times.Once);
        
        // Should not process materials or semi-products
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("MAT001", It.IsAny<CancellationToken>()), 
            Times.Never);
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight("SEMI001", It.IsAny<CancellationToken>()), 
            Times.Never);

        VerifyInfoLog("Product weight recalculation completed. Success: 2, Errors: 0");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithProductWeightClientError_LogsErrorAndContinues()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProduct("PROD001"),
            CreateProduct("PROD002"),
            CreateProduct("PROD003")
        };

        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1.0);
        
        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD002", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to calculate weight"));
        
        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD003", It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.0);

        // Act
        await _service.RecalculateAllProductWeights();

        // Assert
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
            Times.Exactly(3));

        VerifyErrorLog("Failed to recalculate weight for product PROD002");
        VerifyInfoLog("Product weight recalculation completed. Success: 2, Errors: 1");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithCancellationRequested_StopsProcessing()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProduct("PROD001"),
            CreateProduct("PROD002"),
            CreateProduct("PROD003")
        };

        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var cancellationTokenSource = new CancellationTokenSource();
        
        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1.0);
        
        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD002", It.IsAny<CancellationToken>()))
            .Callback(() => cancellationTokenSource.Cancel())
            .ReturnsAsync(2.0);

        // Act
        await _service.RecalculateAllProductWeights(cancellationTokenSource.Token);

        // Assert
        VerifyWarningLog("Product weight recalculation was cancelled after processing 2 products");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithRepositoryError_ThrowsException()
    {
        // Arrange
        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Repository error"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RecalculateAllProductWeights());

        exception.Message.Should().Be("Repository error");
        VerifyErrorLog("Failed to complete product weight recalculation");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithNullWeightReturned_DoesNotUpdateProduct()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProduct("PROD001"),
            CreateProduct("PROD002")
        };

        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((double?)null);
        
        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2.5);

        // Act
        await _service.RecalculateAllProductWeights();

        // Assert
        products[0].NetWeight.Should().BeNull(); // Should not be updated
        products[1].NetWeight.Should().Be(2.5); // Should be updated

        VerifyInfoLog("Product weight recalculation completed. Success: 2, Errors: 0");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_WithEmptyProductList_CompletesSuccessfully()
    {
        // Arrange
        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        // Act
        await _service.RecalculateAllProductWeights();

        // Assert
        _mockProductWeightClient.Verify(
            x => x.RefreshProductWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
            Times.Never);

        VerifyInfoLog("Product weight recalculation completed. Success: 0, Errors: 0");
    }

    [Fact]
    public async Task RecalculateAllProductWeights_UpdatesProductNetWeight_WhenWeightClientReturnsValue()
    {
        // Arrange
        var product = CreateProduct("PROD001");
        product.NetWeight = 1.0; // Initial weight

        _mockCatalogRepository
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        _mockProductWeightClient
            .Setup(x => x.RefreshProductWeight("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2.5);

        // Act
        await _service.RecalculateAllProductWeights();

        // Assert
        product.NetWeight.Should().Be(2.5);
    }

    private CatalogAggregate CreateProduct(string productCode)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product
        };
    }

    private CatalogAggregate CreateMaterial(string productCode)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Material
        };
    }

    private CatalogAggregate CreateSemiProduct(string productCode)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.SemiProduct
        };
    }

    private void VerifyInfoLog(string message)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private void VerifyErrorLog(string message)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private void VerifyWarningLog(string message)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}