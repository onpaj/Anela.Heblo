using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class SubmitManufactureStockTakingHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IErpStockDomainService> _erpStockDomainServiceMock;
    private readonly Mock<ILogger<SubmitManufactureStockTakingHandler>> _loggerMock;
    private readonly SubmitManufactureStockTakingHandler _handler;

    public SubmitManufactureStockTakingHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _erpStockDomainServiceMock = new Mock<IErpStockDomainService>();
        _loggerMock = new Mock<ILogger<SubmitManufactureStockTakingHandler>>();

        _handler = new SubmitManufactureStockTakingHandler(
            _catalogRepositoryMock.Object,
            _loggerMock.Object,
            _erpStockDomainServiceMock.Object);
    }

    #region Product with lots (HasLots = true) tests

    [Fact]
    public async Task Handle_ProductWithLots_ValidLotsProvided_ReturnsSuccess()
    {
        // Arrange
        var productCode = "MAT001";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            Lots = new List<ManufactureStockTakingLotDto>
            {
                new ManufactureStockTakingLotDto
                {
                    LotCode = "LOT001",
                    Expiration = DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
                    Amount = 10.5m,
                    SoftStockTaking = false
                },
                new ManufactureStockTakingLotDto
                {
                    LotCode = "LOT002",
                    Expiration = DateOnly.FromDateTime(DateTime.Today.AddMonths(12)),
                    Amount = 5.0m,
                    SoftStockTaking = true
                }
            }
        };

        var product = CreateMaterialWithLots(productCode);
        var stockTakingRecord = CreateSuccessfulStockTakingRecord(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _erpStockDomainServiceMock.Setup(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()))
            .ReturnsAsync(stockTakingRecord);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Id.Should().Be(stockTakingRecord.Id.ToString());
        response.AmountNew.Should().Be(stockTakingRecord.AmountNew);
        response.AmountOld.Should().Be(stockTakingRecord.AmountOld);

        // Verify correct lots were passed to ERP service
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.Is<ErpStockTakingRequest>(req =>
            req.ProductCode == productCode &&
            req.StockTakingItems.Count == 2 &&
            req.StockTakingItems.First().LotCode == "LOT001" &&
            req.StockTakingItems.First().Amount == 10.5m &&
            req.StockTakingItems.Last().LotCode == "LOT002" &&
            req.StockTakingItems.Last().Amount == 5.0m
        )), Times.Once);
    }

    [Fact]
    public async Task Handle_ProductWithLots_NoLotsProvided_ThrowsArgumentException()
    {
        // Arrange
        var productCode = "MAT001";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = 100m // This should be ignored for lot-based products
        };

        var product = CreateMaterialWithLots(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(request, CancellationToken.None));
        
        // Verify ERP service was never called
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProductWithLots_EmptyLotsProvided_ThrowsArgumentException()
    {
        // Arrange
        var productCode = "MAT001";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            Lots = new List<ManufactureStockTakingLotDto>(), // Empty list
            TargetAmount = 100m // This should be ignored for lot-based products
        };

        var product = CreateMaterialWithLots(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(request, CancellationToken.None));
        
        // Verify ERP service was never called
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()), Times.Never);
    }

    #endregion

    #region Product without lots (HasLots = false) tests

    [Fact]
    public async Task Handle_ProductWithoutLots_ValidTargetAmountProvided_ReturnsSuccess()
    {
        // Arrange
        var productCode = "MAT002";
        var targetAmount = 50.0m;
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = targetAmount,
            SoftStockTaking = false
        };

        var product = CreateMaterialWithoutLots(productCode);
        var stockTakingRecord = CreateSuccessfulStockTakingRecord(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _erpStockDomainServiceMock.Setup(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()))
            .ReturnsAsync(stockTakingRecord);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Id.Should().Be(stockTakingRecord.Id.ToString());
        response.AmountNew.Should().Be(stockTakingRecord.AmountNew);
        response.AmountOld.Should().Be(stockTakingRecord.AmountOld);

        // Verify correct amount was passed to ERP service (single lot without lot code)
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.Is<ErpStockTakingRequest>(req =>
            req.ProductCode == productCode &&
            req.StockTakingItems.Count == 1 &&
            req.StockTakingItems.First().LotCode == null &&
            req.StockTakingItems.First().Amount == targetAmount &&
            req.StockTakingItems.First().SoftStockTaking == false
        )), Times.Once);
    }

    [Fact]
    public async Task Handle_ProductWithoutLots_NoTargetAmountProvided_ThrowsArgumentException()
    {
        // Arrange
        var productCode = "MAT002";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode
            // No TargetAmount provided
        };

        var product = CreateMaterialWithoutLots(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(request, CancellationToken.None));
        
        // Verify ERP service was never called
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProductWithoutLots_LotsProvidedButIgnored_UsesTargetAmount()
    {
        // Arrange
        var productCode = "MAT002";
        var targetAmount = 75.5m;
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = targetAmount,
            Lots = new List<ManufactureStockTakingLotDto> // These should be ignored
            {
                new ManufactureStockTakingLotDto
                {
                    LotCode = "IGNORED",
                    Amount = 999m
                }
            }
        };

        var product = CreateMaterialWithoutLots(productCode);
        var stockTakingRecord = CreateSuccessfulStockTakingRecord(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _erpStockDomainServiceMock.Setup(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()))
            .ReturnsAsync(stockTakingRecord);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();

        // Verify that lots were ignored and only TargetAmount was used
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.Is<ErpStockTakingRequest>(req =>
            req.ProductCode == productCode &&
            req.StockTakingItems.Count == 1 &&
            req.StockTakingItems.First().LotCode == null &&
            req.StockTakingItems.First().Amount == targetAmount // NOT 999m from lots
        )), Times.Once);
    }

    #endregion

    #region General tests

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsErrorResponse()
    {
        // Arrange
        var productCode = "NONEXISTENT";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = 100m
        };

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductNotFound);
        response.Params["ProductCode"].Should().Be(productCode);
        
        // Verify ERP service was never called
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProductIsNotMaterial_ReturnsErrorResponse()
    {
        // Arrange
        var productCode = "PRODUCT001";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = 100m
        };

        var product = CreateNonMaterialProduct(productCode);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        response.Params["ProductCode"].Should().Be(productCode);
        response.Params["ProductType"].Should().Be(ProductType.Product.ToString());
        response.Params["Message"].Should().Be("Manufacture stock taking only supports materials");
        
        // Verify ERP service was never called
        _erpStockDomainServiceMock.Verify(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ErpStockTakingFailed_ReturnsErrorResponse()
    {
        // Arrange
        var productCode = "MAT001";
        var request = new SubmitManufactureStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = 100m
        };

        var product = CreateMaterialWithoutLots(productCode);
        var failedStockTakingRecord = CreateFailedStockTakingRecord();

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _erpStockDomainServiceMock.Setup(x => x.SubmitStockTakingAsync(It.IsAny<ErpStockTakingRequest>()))
            .ReturnsAsync(failedStockTakingRecord);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.StockTakingFailed);
        response.Params["ProductCode"].Should().Be(productCode);
        response.Params["Error"].Should().Be("Stock taking failed in ERP");
    }

    #endregion

    #region Helper methods

    private CatalogAggregate CreateMaterialWithLots(string productCode)
    {
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = $"Material {productCode}",
            Type = ProductType.Material,
            Stock = new StockData(),
            Location = "A1",
            HasLots = true
        };
        
        return product;
    }

    private CatalogAggregate CreateMaterialWithoutLots(string productCode)
    {
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = $"Material {productCode}",
            Type = ProductType.Material,
            Stock = new StockData(),
            Location = "B2",
            HasLots = false
        };
        
        return product;
    }

    private CatalogAggregate CreateNonMaterialProduct(string productCode)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = $"Product {productCode}",
            Type = ProductType.Product, // Not a material
            Stock = new StockData(),
            Location = "C3",
            HasLots = false
        };
    }

    private StockTakingRecord CreateSuccessfulStockTakingRecord(string productCode)
    {
        return new StockTakingRecord
        {
            Id = 1001,
            Code = productCode,
            Type = StockTakingType.Erp,
            AmountOld = 25.0,
            AmountNew = 50.0,
            Date = DateTime.UtcNow,
            User = "TestUser",
            Error = null // No error = success
        };
    }

    private StockTakingRecord CreateFailedStockTakingRecord()
    {
        return new StockTakingRecord
        {
            Id = 1002,
            Code = "ST002",
            Type = StockTakingType.Erp,
            AmountOld = 25.0,
            AmountNew = 25.0,
            Date = DateTime.Now,
            User = "TestUser",
            Error = "Stock taking failed in ERP"
        };
    }

    #endregion
}