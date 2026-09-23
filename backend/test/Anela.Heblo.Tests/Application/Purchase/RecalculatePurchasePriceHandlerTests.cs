using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Purchase;

public class RecalculatePurchasePriceHandlerTests
{
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IPurchasePriceRecalculationService> _priceRecalculationServiceMock;
    private readonly Mock<IPurchasePriceSyncSource> _priceSyncSourceMock;
    private readonly Mock<ILogger<RecalculatePurchasePriceHandler>> _loggerMock;
    private readonly RecalculatePurchasePriceHandler _handler;

    public RecalculatePurchasePriceHandlerTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _priceRecalculationServiceMock = new Mock<IPurchasePriceRecalculationService>();
        _loggerMock = new Mock<ILogger<RecalculatePurchasePriceHandler>>();

        _priceSyncSourceMock = new Mock<IPurchasePriceSyncSource>();
        _priceSyncSourceMock
            .Setup(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PurchasePriceSyncCandidate>());

        _handler = new RecalculatePurchasePriceHandler(
            _materialCatalogMock.Object,
            _priceRecalculationServiceMock.Object,
            _priceSyncSourceMock.Object,
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

        _priceRecalculationServiceMock.Setup(x => x.RecalculatePurchasePriceAsync(123, It.IsAny<CancellationToken>()))
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

        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(123, It.IsAny<CancellationToken>()), Times.Once);
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

        _priceRecalculationServiceMock.Setup(x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
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

        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(123, It.IsAny<CancellationToken>()), Times.Once);
        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(456, It.IsAny<CancellationToken>()), Times.Once);
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

        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _priceRecalculationServiceMock.Setup(x => x.RecalculatePurchasePriceAsync(123, It.IsAny<CancellationToken>()))
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

        _priceRecalculationServiceMock.Setup(x => x.RecalculatePurchasePriceAsync(123, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _priceRecalculationServiceMock.Setup(x => x.RecalculatePurchasePriceAsync(456, It.IsAny<CancellationToken>()))
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

        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _priceRecalculationServiceMock.Setup(x => x.RecalculatePurchasePriceAsync(123, cancellationToken))
            .Returns(Task.CompletedTask);

        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = productCode
        };

        // Act
        await _handler.Handle(request, cancellationToken);

        // Assert
        _materialCatalogMock.Verify(x => x.GetByIdAsync(productCode, cancellationToken), Times.Once);
        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(123, cancellationToken), Times.Once);
    }

    private static PurchasePriceSyncCandidate Candidate(
        string code, int erpItemId, decimal current, decimal? stock,
        MaterialProductType type = MaterialProductType.Material) => new()
        {
            ProductCode = code,
            ProductType = type,
            ErpItemId = erpItemId,
            CurrentPurchasePrice = current,
            StockPrice = stock,
        };

    private void GivenCandidates(params PurchasePriceSyncCandidate[] candidates) =>
        _priceSyncSourceMock
            .Setup(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

    private void GivenBoms(params MaterialBomReference[] boms) =>
        _materialCatalogMock
            .Setup(x => x.GetMaterialsWithBomAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(boms);

    private static RecalculatePurchasePriceRequest RecalculateAll() => new() { RecalculateAll = true };

    [Fact]
    public async Task RecalculateAll_writes_stock_price_for_materials_and_goods_that_differ()
    {
        // Arrange
        GivenCandidates(
            Candidate("AKL097", 789, 3.048594m, 0.311m),
            Candidate("ZBO001", 12, 100m, 80m, MaterialProductType.Goods));
        GivenBoms();

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _priceRecalculationServiceMock.Verify(x => x.SetPurchasePriceAsync(789, 0.311m, It.IsAny<CancellationToken>()), Times.Once);
        _priceRecalculationServiceMock.Verify(x => x.SetPurchasePriceAsync(12, 80m, It.IsAny<CancellationToken>()), Times.Once);
        result.PriceSync.Candidates.Should().Be(2);
        result.PriceSync.Written.Should().Be(2);
    }

    [Fact]
    public async Task RecalculateAll_skips_prices_within_tolerance()
    {
        // Arrange
        GivenCandidates(Candidate("AKL097", 789, 0.31100m, 0.31105m));
        GivenBoms();

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _priceRecalculationServiceMock.Verify(
            x => x.SetPurchasePriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        result.PriceSync.Unchanged.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecalculateAll_skips_items_without_a_usable_stock_price(int? stockPrice)
    {
        // Arrange
        GivenCandidates(Candidate("AKL097", 789, 3m, stockPrice));
        GivenBoms();

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _priceRecalculationServiceMock.Verify(
            x => x.SetPurchasePriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        result.PriceSync.SkippedNoStockPrice.Should().Be(1);
    }

    [Fact]
    public async Task RecalculateAll_runs_writes_then_semi_products_then_products()
    {
        // Arrange
        var calls = new List<string>();
        GivenCandidates(Candidate("AKL097", 789, 3m, 0.3m));
        GivenBoms(
            new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 },
            new MaterialBomReference { ProductCode = "DEZ001001M", BoMId = 5700, IsSemiProduct = true });
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback<int, decimal, CancellationToken>((id, _, _) => calls.Add($"write:{id}"))
            .Returns(Task.CompletedTask);
        _priceRecalculationServiceMock
            .Setup(x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((bomId, _) => calls.Add($"bom:{bomId}"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        calls.Should().Equal("write:789", "bom:5700", "bom:5654");
        result.SemiProducts.Succeeded.Should().Be(1);
        result.Products.Succeeded.Should().Be(1);
        result.SuccessCount.Should().Be(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task RecalculateAll_does_not_recalculate_boms_when_candidates_cannot_be_loaded()
    {
        // Arrange
        _priceSyncSourceMock
            .Setup(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("flexi down"));
        GivenBoms(new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 });

        // Act
        var act = () => _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _priceRecalculationServiceMock.Verify(
            x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecalculateAll_logs_phase1_summary_as_warning_when_a_write_fails()
    {
        // Arrange
        GivenCandidates(Candidate("BAD", 1, 3m, 0.3m));
        GivenBoms();
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(1, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rejected"));

        // Act
        await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Purchase price phase 1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RecalculateAll_logs_phase1_summary_as_information_when_nothing_fails()
    {
        // Arrange
        GivenCandidates(Candidate("AKL097", 789, 3.048594m, 0.311m));
        GivenBoms();

        // Act
        await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Purchase price phase 1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Purchase price phase 1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task RecalculateAll_continues_after_a_failed_write()
    {
        // Arrange
        GivenCandidates(Candidate("BAD", 1, 3m, 0.3m), Candidate("GOOD", 2, 3m, 0.3m));
        GivenBoms(new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 });
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(1, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rejected"));

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        result.PriceSync.Failed.Should().Be(1);
        result.PriceSync.Written.Should().Be(1);
        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(5654, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculateAll_runs_products_even_when_a_semi_product_fails()
    {
        // Arrange
        GivenBoms(
            new MaterialBomReference { ProductCode = "DEZ001001M", BoMId = 5700, IsSemiProduct = true },
            new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 });
        _priceRecalculationServiceMock
            .Setup(x => x.RecalculatePurchasePriceAsync(5700, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        result.SemiProducts.Failed.Should().Be(1);
        result.Products.Succeeded.Should().Be(1);
        result.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task RecalculateAll_propagates_cancellation_during_price_sync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        GivenCandidates(Candidate("A", 1, 3m, 0.3m), Candidate("B", 2, 3m, 0.3m));
        GivenBoms();
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(1, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => _handler.Handle(RecalculateAll(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _priceRecalculationServiceMock.Verify(
            x => x.SetPurchasePriceAsync(2, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SingleProduct_does_not_sync_prices()
    {
        // Arrange
        _materialCatalogMock.Setup(x => x.GetByIdAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMaterialWithBoM("PROD001", 123));

        // Act
        await _handler.Handle(new RecalculatePurchasePriceRequest { ProductCode = "PROD001" }, CancellationToken.None);

        // Assert
        _priceSyncSourceMock.Verify(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()), Times.Never);
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