using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Purchase;

public class RecalculatePurchasePriceHandlerTests
{
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IProductPriceErpClient> _productPriceClientMock;
    private readonly Mock<ILogger<RecalculatePurchasePriceHandler>> _loggerMock;
    private readonly RecalculatePurchasePriceHandler _handler;

    public RecalculatePurchasePriceHandlerTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _productPriceClientMock = new Mock<IProductPriceErpClient>();
        _loggerMock = new Mock<ILogger<RecalculatePurchasePriceHandler>>();

        _handler = new RecalculatePurchasePriceHandler(
            _materialCatalogMock.Object,
            _productPriceClientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidSingleProduct_ShouldRecalculateSuccessfully()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateMaterialWithBoM(productCode, 123);

        _materialCatalogMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

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
        processedProduct.Success.Should().BeTrue();
        processedProduct.ErrorCode.Should().BeNull();

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithRecalculateAll_ShouldProcessOnlyProductsWithBoM()
    {
        // Arrange
        var bomReferences = new List<MaterialBomReference>
        {
            new MaterialBomReference { ProductCode = "PROD001", BoMId = 123 },
            new MaterialBomReference { ProductCode = "PROD002", BoMId = 456 }
        };

        _materialCatalogMock.Setup(x => x.GetMaterialsWithBomAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomReferences);

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
        result.TotalCount.Should().Be(2);
        result.IsSuccess.Should().BeTrue();
        result.ProcessedProducts.Should().HaveCount(2);

        result.ProcessedProducts.Should().OnlyContain(p => p.Success);
        result.ProcessedProducts.Select(p => p.ProductCode).Should().BeEquivalentTo(new[] { "PROD001", "PROD002" });

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(123, It.IsAny<CancellationToken>()), Times.Once);
        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(456, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithSingleProductNotFound_ShouldReturnError()
    {
        // Arrange
        _materialCatalogMock.Setup(x => x.GetByIdAsync("NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MaterialInfo?)null);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = "NONEXISTENT",
            RecalculateAll = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CatalogItemNotFound);
        result.Params.Should().ContainKey("ProductCode");
        result.Params["ProductCode"].Should().Be("NONEXISTENT");

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithSingleProductWithoutBoM_ShouldFail()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateMaterialWithoutBoM(productCode);

        _materialCatalogMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = productCode,
            RecalculateAll = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        result.Params.Should().ContainKey("Message");
        result.Params["Message"].Should().Contain("does not have BoM");

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithErpClientFailure_ShouldRecordError()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateMaterialWithBoM(productCode, 123);

        _materialCatalogMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

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
        processedProduct.Success.Should().BeFalse();
        processedProduct.ErrorCode.Should().Be(ErrorCodes.Exception);
        processedProduct.Params.Should().ContainKey("message");
        processedProduct.Params["message"].Should().Be(expectedError);
    }

    [Fact]
    public async Task Handle_WithMixedSuccessAndFailure_ShouldRecordBoth()
    {
        // Arrange
        var bomReferences = new List<MaterialBomReference>
        {
            new MaterialBomReference { ProductCode = "PROD001", BoMId = 123 },
            new MaterialBomReference { ProductCode = "PROD002", BoMId = 456 }
        };

        _materialCatalogMock.Setup(x => x.GetMaterialsWithBomAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomReferences);

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
        successResult.Success.Should().BeTrue();
        successResult.ErrorCode.Should().BeNull();

        var failResult = result.ProcessedProducts.First(p => p.ProductCode == "PROD002");
        failResult.Success.Should().BeFalse();
        failResult.ErrorCode.Should().Be(ErrorCodes.Exception);
        failResult.Params.Should().ContainKey("message");
        failResult.Params["message"].Should().Be("Network timeout");
    }

    [Fact]
    public async Task Handle_WithNoProductsWithBoM_ShouldReturnEmptyResult()
    {
        // Arrange
        var bomReferences = new List<MaterialBomReference>();

        _materialCatalogMock.Setup(x => x.GetMaterialsWithBomAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomReferences);

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
        result.IsSuccess.Should().BeFalse();
        result.ProcessedProducts.Should().BeEmpty();
        result.Message.Should().Be("No products found to recalculate");

        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldReturnError()
    {
        // Arrange
        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = null,
            RecalculateAll = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        result.Params.Should().ContainKey("Message");
        result.Params["Message"].Should().Be("Either ProductCode must be specified or RecalculateAll must be true");
    }

    [Fact]
    public async Task Handle_WithCancellationToken_ShouldPassTokenToClients()
    {
        // Arrange
        var productCode = "PROD001";
        var product = CreateMaterialWithBoM(productCode, 123);
        var cancellationToken = new CancellationToken();

        _materialCatalogMock.Setup(x => x.GetByIdAsync(productCode, cancellationToken))
            .ReturnsAsync(product);

        _productPriceClientMock.Setup(x => x.RecalculatePurchasePrice(123, cancellationToken))
            .Returns(Task.CompletedTask);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = productCode
        };

        // Act
        await _handler.Handle(request, cancellationToken);

        // Assert
        _materialCatalogMock.Verify(x => x.GetByIdAsync(productCode, cancellationToken), Times.Once);
        _productPriceClientMock.Verify(x => x.RecalculatePurchasePrice(123, cancellationToken), Times.Once);
    }

    private static MaterialInfo CreateMaterialWithBoM(string productCode, int bomId)
    {
        return new MaterialInfo
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            HasBoM = true,
            BoMId = bomId
        };
    }

    private static MaterialInfo CreateMaterialWithoutBoM(string productCode)
    {
        return new MaterialInfo
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            HasBoM = false,
            BoMId = null
        };
    }
}