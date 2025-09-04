using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Purchase;

public class RecalculatePurchasePriceHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IProductPriceErpClient> _productPriceClientMock;
    private readonly Mock<ILogger<RecalculatePurchasePriceHandler>> _loggerMock;
    private readonly RecalculatePurchasePriceHandler _handler;

    public RecalculatePurchasePriceHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _productPriceClientMock = new Mock<IProductPriceErpClient>();
        _loggerMock = new Mock<ILogger<RecalculatePurchasePriceHandler>>();

        _handler = new RecalculatePurchasePriceHandler(
            _catalogRepositoryMock.Object,
            _productPriceClientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidSingleProduct_ShouldRecalculateSuccessfully()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateProductWithBoM(productCode, 123);
        var products = new List<CatalogAggregate> { product };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = productCode,
            RecalculateAll = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(1);
        result.IsSuccess.Should().BeTrue();
        result.ProcessedProducts.Should().HaveCount(1);

        var processedProduct = result.ProcessedProducts.First();
        processedProduct.ProductCode.Should().Be(productCode);
        processedProduct.IsSuccess.Should().BeTrue();
        processedProduct.ErrorMessage.Should().BeNull();

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithRecalculateAll_ShouldProcessOnlyProductsWithBoM()
    {
        // Arrange
        var productWithBoM1 = CreateProductWithBoM("PROD001", 123);
        var productWithBoM2 = CreateProductWithBoM("PROD002", 456);
        var productWithoutBoM = CreateProductWithoutBoM("PROD003");
        var products = new List<CatalogAggregate> { productWithBoM1, productWithBoM2, productWithoutBoM };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RecalculatePurchasePriceRequest
        {
            RecalculateAll = true
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(2); // Only products with BoM
        result.IsSuccess.Should().BeTrue();
        result.ProcessedProducts.Should().HaveCount(2);

        result.ProcessedProducts.Should().OnlyContain(p => p.IsSuccess);
        result.ProcessedProducts.Select(p => p.ProductCode).Should().BeEquivalentTo(new[] { "PROD001", "PROD002" });

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()), Times.Once);
        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(456, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithSingleProductNotFound_ShouldThrowArgumentException()
    {
        // Arrange
        var products = new List<CatalogAggregate>();

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = "NONEXISTENT",
            RecalculateAll = false
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(request, CancellationToken.None));

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithSingleProductWithoutBoM_ShouldFail()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateProductWithoutBoM(productCode);
        var products = new List<CatalogAggregate> { product };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = productCode,
            RecalculateAll = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.TotalCount.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
        result.ProcessedProducts.Should().HaveCount(1);

        var processedProduct = result.ProcessedProducts.First();
        processedProduct.ProductCode.Should().Be(productCode);
        processedProduct.IsSuccess.Should().BeFalse();
        processedProduct.ErrorMessage.Should().Contain("does not have BoM");

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithErpClientFailure_ShouldRecordError()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateProductWithBoM(productCode, 123);
        var products = new List<CatalogAggregate> { product };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var expectedError = "ERP system unavailable";
        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(expectedError));

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = productCode,
            RecalculateAll = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.TotalCount.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
        result.ProcessedProducts.Should().HaveCount(1);

        var processedProduct = result.ProcessedProducts.First();
        processedProduct.ProductCode.Should().Be(productCode);
        processedProduct.IsSuccess.Should().BeFalse();
        processedProduct.ErrorMessage.Should().Be(expectedError);
    }

    [Fact]
    public async Task Handle_WithMixedSuccessAndFailure_ShouldRecordBoth()
    {
        // Arrange
        var successProduct = CreateProductWithBoM("PROD001", 123);
        var failProduct = CreateProductWithBoM("PROD002", 456);
        var products = new List<CatalogAggregate> { successProduct, failProduct };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(456, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network timeout"));

        var request = new RecalculatePurchasePriceRequest
        {
            RecalculateAll = true
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.TotalCount.Should().Be(2);
        result.IsSuccess.Should().BeFalse();
        result.ProcessedProducts.Should().HaveCount(2);

        var successResult = result.ProcessedProducts.First(p => p.ProductCode == "PROD001");
        successResult.IsSuccess.Should().BeTrue();
        successResult.ErrorMessage.Should().BeNull();

        var failResult = result.ProcessedProducts.First(p => p.ProductCode == "PROD002");
        failResult.IsSuccess.Should().BeFalse();
        failResult.ErrorMessage.Should().Be("Network timeout");
    }

    [Fact]
    public async Task Handle_WithNoProductsWithBoM_ShouldReturnEmptyResult()
    {
        // Arrange
        var productWithoutBoM1 = CreateProductWithoutBoM("PROD001");
        var productWithoutBoM2 = CreateProductWithoutBoM("PROD002");
        var products = new List<CatalogAggregate> { productWithoutBoM1, productWithoutBoM2 };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var request = new RecalculatePurchasePriceRequest
        {
            RecalculateAll = true
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
        result.IsSuccess.Should().BeFalse(); // No products found to recalculate
        result.ProcessedProducts.Should().BeEmpty();
        result.Message.Should().Be("No products found to recalculate");

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = null, // Neither product code nor recalculate all
            RecalculateAll = false
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithCancellationToken_ShouldPassTokenToClients()
    {
        // Arrange
        var product = CreateProductWithBoM("PROD001", 123);
        var products = new List<CatalogAggregate> { product };
        var cancellationToken = new CancellationToken();

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(cancellationToken))
            .ReturnsAsync(products);

        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(123, cancellationToken))
            .Returns(Task.CompletedTask);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = "PROD001"
        };

        // Act
        await _handler.Handle(request, cancellationToken);

        // Assert
        _catalogRepositoryMock.Verify(x => x.GetAllAsync(cancellationToken), Times.Once);
        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(123, cancellationToken), Times.Once);
    }

    private static CatalogAggregate CreateProductWithBoM(string productCode, int bomId)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ErpPrice = new ProductPriceErp
            {
                BoMId = bomId,
                PurchasePrice = 100.00m
            }
        };
    }

    private static CatalogAggregate CreateProductWithoutBoM(string productCode)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ErpPrice = new ProductPriceErp
            {
                BoMId = null,
                PurchasePrice = 100.00m
            }
        };
    }
}