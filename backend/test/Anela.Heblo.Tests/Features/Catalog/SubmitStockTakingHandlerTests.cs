using Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class SubmitStockTakingHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();
    private readonly Mock<IEshopStockDomainService> _eshopStockDomainServiceMock = new();
    private readonly Mock<ILogger<SubmitStockTakingHandler>> _loggerMock = new();
    private readonly SubmitStockTakingHandler _handler;

    public SubmitStockTakingHandlerTests()
    {
        _handler = new SubmitStockTakingHandler(
            _catalogRepositoryMock.Object,
            _loggerMock.Object,
            _eshopStockDomainServiceMock.Object);
    }

    [Fact]
    public async Task Handle_DomainServiceReturnsError_ReturnsStockTakingFailedResponse()
    {
        // Arrange
        var request = CreateRequest("PROD-001");
        var errorRecord = CreateErrorRecord("PROD-001", "Eshop rejected");

        _eshopStockDomainServiceMock
            .Setup(s => s.SubmitStockTakingAsync(It.Is<EshopStockTakingRequest>(r =>
                r.ProductCode == "PROD-001" &&
                r.TargetAmount == 100m &&
                r.SoftStockTaking == false)))
            .ReturnsAsync(errorRecord);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.StockTakingFailed);
        response.Params!["ProductCode"].Should().Be("PROD-001");
        response.Params!["Error"].Should().Be("Eshop rejected");
        _catalogRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DomainServiceSucceeds_ProductNotFoundInCatalog_ReturnsSuccessWithoutSyncing()
    {
        // Arrange
        var request = CreateRequest("PROD-001");
        var successRecord = CreateSuccessRecord("PROD-001");

        _eshopStockDomainServiceMock
            .Setup(s => s.SubmitStockTakingAsync(It.IsAny<EshopStockTakingRequest>()))
            .ReturnsAsync(successRecord);

        _catalogRepositoryMock
            .Setup(r => r.GetByIdAsync("PROD-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Id.Should().Be(successRecord.Id);
        response.Type.Should().Be(successRecord.Type);
        response.Code.Should().Be(successRecord.Code);
        response.AmountNew.Should().Be(successRecord.AmountNew);
        response.AmountOld.Should().Be(successRecord.AmountOld);
        response.Date.Should().Be(successRecord.Date);
        response.User.Should().Be(successRecord.User);
        response.Error.Should().Be(successRecord.Error);

        _catalogRepositoryMock.Verify(r => r.GetByIdAsync("PROD-001", It.IsAny<CancellationToken>()), Times.Once);
        // SyncStockTaking is unreachable: product == null. This documents the silent-correctness hole — when eshop stock-taking succeeds but the product is not in the local catalog snapshot, the handler silently returns success without syncing. See spec FR-2.
    }

    [Fact]
    public async Task Handle_DomainServiceSucceeds_ProductFound_CallsSyncStockTakingAndReturnsResponse()
    {
        // Arrange
        var request = CreateRequest("PROD-001");
        var successRecord = CreateSuccessRecord("PROD-001");
        var aggregate = CreateAggregate("PROD-001");
        CancellationToken capturedCancellationToken = CancellationToken.None;

        _eshopStockDomainServiceMock
            .Setup(s => s.SubmitStockTakingAsync(It.IsAny<EshopStockTakingRequest>()))
            .ReturnsAsync(successRecord);

        _catalogRepositoryMock
            .Setup(r => r.GetByIdAsync("PROD-001", It.IsAny<CancellationToken>()))
            .Callback((string _, CancellationToken ct) => capturedCancellationToken = ct)
            .ReturnsAsync(aggregate);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Id.Should().Be(successRecord.Id);
        response.Type.Should().Be(successRecord.Type);
        response.Code.Should().Be(successRecord.Code);
        response.AmountNew.Should().Be(successRecord.AmountNew);
        response.AmountOld.Should().Be(successRecord.AmountOld);
        response.Date.Should().Be(successRecord.Date);
        response.User.Should().Be(successRecord.User);
        response.Error.Should().Be(successRecord.Error);

        aggregate.StockTakingHistory.Should().Contain(successRecord);
        aggregate.Stock.Eshop.Should().Be((decimal)successRecord.AmountNew);

        _catalogRepositoryMock.Verify(r => r.GetByIdAsync("PROD-001", capturedCancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_DomainServiceThrows_ReturnsInternalServerErrorResponse()
    {
        // Arrange
        var request = CreateRequest("PROD-001");
        var exception = new InvalidOperationException("boom");

        _eshopStockDomainServiceMock
            .Setup(s => s.SubmitStockTakingAsync(It.IsAny<EshopStockTakingRequest>()))
            .ThrowsAsync(exception);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        response.Params!["ProductCode"].Should().Be("PROD-001");
        _catalogRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SubmitStockTakingRequest CreateRequest(string productCode, decimal targetAmount = 100m, bool soft = false)
    {
        return new SubmitStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = targetAmount,
            SoftStockTaking = soft
        };
    }

    private static StockTakingRecord CreateSuccessRecord(string productCode)
    {
        return new StockTakingRecord
        {
            Id = 42,
            Type = StockTakingType.Eshop,
            Code = productCode,
            AmountNew = 150.5,
            AmountOld = 100.0,
            Date = new DateTime(2026, 6, 8),
            User = "test-user",
            Error = null
        };
    }

    private static StockTakingRecord CreateErrorRecord(string productCode, string error)
    {
        return new StockTakingRecord
        {
            Id = 0,
            Type = StockTakingType.Eshop,
            Code = productCode,
            AmountNew = 0,
            AmountOld = 0,
            Date = default,
            User = null,
            Error = error
        };
    }

    private static CatalogAggregate CreateAggregate(string productCode)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            Type = ProductType.Product,
            Stock = new StockData()
        };
    }
}
